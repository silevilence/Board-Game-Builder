using System.Security.Cryptography;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Networking;

public sealed record OnlineIdentity(int Protocol, string Fingerprint)
{
    public static OnlineIdentity For(LoadedModule module) => new(1, module.Fingerprint);
}
public sealed record SeatLease(string Token, string Seat, string ResumeToken);
public enum DisconnectPolicy { Wait, EndSession, Cpu }
public sealed record AuthorityTimer(string Seat, string ActionId, TimeSpan Interval);
public sealed record OnlineOptions(TimeSpan IdleTimeout, TimeSpan ReconnectGrace, DisconnectPolicy OnTimeout = DisconnectPolicy.Wait,
    IReadOnlyList<AuthorityTimer>? Timers = null, int MaxRevisions = 4096, int MaxObservers = 32)
{
    public static OnlineOptions Default => new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2));
}
public sealed record OnlineResume(SeatLease Lease, long NextSequence, IReadOnlyList<OnlineFrame> Frames);
public sealed record OnlineFrame(long Revision, SeatView View, IReadOnlyList<ActionOffer> Actions,
    bool Started, bool Closed, string? Fault, PlayerEvent? Event = null);
public sealed record OnlineProposal(long Sequence, long ExpectedRevision, string Actor, Command Command);
public sealed record OnlineReceipt(long Sequence, long NextSequence, CommandResult Result, OnlineFrame Frame);
public sealed record OnlineHostStatus(long Revision, bool Ended, bool Closed, string? Fault);
public sealed class OnlineException(string code) : Exception(code);

// Only the trusted host owns this object. Remote callers receive detached projections.
public sealed class OnlineSession : IDisposable
{
    private readonly object gate = new();
    private readonly IGameSession game;
    private readonly Dictionary<string, string> controllers;
    private sealed class MemberState(SeatLease lease)
    {
        public SeatLease Lease { get; set; } = lease;
        public DateTimeOffset LastSeen { get; set; }
        public DateTimeOffset? DisconnectedAt { get; set; }
        public bool TakenOver { get; set; }
        public long NextSequence { get; set; } = 1;
        public string? LastHash { get; set; }
        public OnlineReceipt? LastReceipt { get; set; }
    }
    private readonly Dictionary<string, MemberState> members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<OnlineFrame>> history = new(StringComparer.Ordinal);
    private readonly OnlineOptions options;
    private readonly TimeProvider time;
    private readonly Dictionary<AuthorityTimer, DateTimeOffset> timerDue = [];
    private bool started;
    private bool closed;
    private long revision;
    public OnlineIdentity Identity { get; }

    public OnlineSession(LoadedModule module, uint seed, IReadOnlyDictionary<string, string> controllers,
        OnlineOptions? options = null, TimeProvider? time = null)
    {
        this.options = options ?? OnlineOptions.Default;
        this.time = time ?? TimeProvider.System;
        if (this.options.IdleTimeout <= TimeSpan.Zero || this.options.ReconnectGrace < TimeSpan.Zero || !Enum.IsDefined(this.options.OnTimeout))
            throw new ArgumentException("Invalid disconnect configuration.");
        if (this.options.MaxRevisions < 10 || this.options.MaxRevisions > 100000 || this.options.MaxObservers < 0 || this.options.MaxObservers > 256)
            throw new ArgumentException("Invalid session resource limits.");
        if (module.Initial.Seats.Any(s => !controllers.ContainsKey(s.Id)) ||
            controllers.Count != module.Initial.Seats.Count || controllers.Values.Any(v => v is not ("human" or "cpu")))
            throw new ArgumentException("Every seat requires a human or cpu controller.");
        Identity = OnlineIdentity.For(module);
        this.controllers = new(controllers);
        foreach (var timer in this.options.Timers ?? [])
        {
            if (!controllers.ContainsKey(timer.Seat) || string.IsNullOrWhiteSpace(timer.ActionId) || timer.Interval < TimeSpan.FromMilliseconds(100))
                throw new ArgumentException("Invalid authority action timer.");
            timerDue.Add(timer, this.time.GetUtcNow() + timer.Interval);
        }
        game = new PlaySession(module, seed, controllers);
        try
        {
            if (this.options.OnTimeout == DisconnectPolicy.Cpu)
                foreach (var seat in controllers.Keys) { game.SetController(seat, "cpu"); game.SetController(seat, controllers[seat]); }
        }
        catch { game.Dispose(); throw; }
        started = !controllers.Values.Contains("human");
        foreach (var seat in controllers.Keys) history.Add(seat, []);
        history.Add("", []); // An audience is not a playable seat.
        Capture();
    }

    public SeatLease Join(OnlineIdentity identity, string? requestedSeat = null)
    {
        lock (gate)
        {
            EnsureOpen();
            if (identity != Identity) throw new OnlineException("version-mismatch");
            var seat = controllers.Keys.FirstOrDefault(s => controllers[s] == "human" &&
                (requestedSeat == null || s == requestedSeat) && !members.Values.Any(m => m.Lease.Seat == s));
            if (seat == null) throw new OnlineException("seat-unavailable");
            var lease = new SeatLease(NewToken(), seat, NewToken());
            members.Add(lease.Token, new(lease) { LastSeen = this.time.GetUtcNow() });
            started = controllers.Where(p => p.Value == "human").All(p => members.Values.Any(m => m.Lease.Seat == p.Key));
            Advance();
            return lease;
        }
    }

    public OnlineFrame Read(string token)
    {
        lock (gate) return Wire.Copy(history[Member(token).Lease.Seat][^1]);
    }

    public SeatLease Watch(OnlineIdentity identity)
    {
        lock (gate)
        {
            EnsureOpen();
            if (identity != Identity) throw new OnlineException("version-mismatch");
            if (members.Values.Count(m => m.Lease.Seat == "") >= options.MaxObservers) throw new OnlineException("audience-limit");
            var lease = new SeatLease(NewToken(), "", NewToken());
            members.Add(lease.Token, new(lease) { LastSeen = time.GetUtcNow() });
            return lease;
        }
    }

    public OnlineReceipt Submit(string token, OnlineProposal proposal)
    {
        lock (gate)
        {
            EnsureOpen();
            var member = Member(token);
            if (member.Lease.Seat == "") throw new OnlineException("audience-read-only");
            if (proposal.Actor != member.Lease.Seat) throw new OnlineException("identity-mismatch");
            var hash = Wire.Hash(Wire.Json(proposal));
            if (proposal.Sequence == member.NextSequence - 1 && member.LastReceipt != null)
            {
                if (hash != member.LastHash) throw new OnlineException("sequence-conflict");
                return Wire.Copy(member.LastReceipt);
            }
            if (proposal.Sequence != member.NextSequence) throw new OnlineException("out-of-order");
            if (!started) throw new OnlineException("waiting-for-players");
            var offered = game.Actions(member.Lease.Seat);
            var commandJson = Wire.Json(proposal.Command);
            var available = offered.Any(a => !timerDue.Keys.Any(t => t.Seat == member.Lease.Seat && t.ActionId == a.Id) && Wire.Json(a.Command) == commandJson);
            var result = proposal.ExpectedRevision != revision ? new CommandResult(0, false, "stale-revision")
                : !available ? new CommandResult(0, false, "operation-not-offered") : game.SubmitHuman(member.Lease.Seat, proposal.Command);
            // Script diagnostics and commands belong only in the host recording.
            if (!result.Accepted && result.Reason != "stale-revision") result = result with { Reason = "operation-rejected" };
            Advance(new(result.Sequence, member.Lease.Seat, result.Accepted, result.Reason));
            member.NextSequence++;
            member.LastHash = hash;
            member.LastReceipt = new(proposal.Sequence, member.NextSequence, result, history[member.Lease.Seat][^1]);
            return Wire.Copy(member.LastReceipt);
        }
    }

    public bool StepCpu()
    {
        lock (gate)
        {
            if (closed || !started || !game.IsCpu || game.Ended || game.Fault != null) return false;
            var result = game.StepCpu();
            Advance(new(game.ExportAuthorityLog().EntryCount, "", result, result ? "accepted" : "cpu-failure"));
            return result;
        }
    }

    public void Leave(string token)
    {
        lock (gate)
        {
            var member = Member(token);
            members.Remove(token);
            if (member.Lease.Seat == "") return;
            closed = true; // Explicit leave ends the session; never silently substitutes a CPU.
            Advance();
        }
    }

    public void Disconnect(string token)
    {
        lock (gate) Member(token).DisconnectedAt = time.GetUtcNow();
    }

    public OnlineResume Resume(OnlineIdentity identity, string resumeToken, long afterRevision)
    {
        lock (gate)
        {
            if (identity != Identity) throw new OnlineException("version-mismatch");
            var member = members.Values.FirstOrDefault(m => m.Lease.ResumeToken == resumeToken)
                ?? throw new OnlineException("invalid-identity");
            if (afterRevision < -1 || afterRevision > revision) throw new OnlineException("invalid-cursor");
            members.Remove(member.Lease.Token);
            member.Lease = member.Lease with { Token = NewToken() };
            members.Add(member.Lease.Token, member);
            member.LastSeen = time.GetUtcNow();
            member.DisconnectedAt = null;
            if (member.TakenOver && !closed)
            {
                game.SetController(member.Lease.Seat, "human");
                controllers[member.Lease.Seat] = "human";
                member.TakenOver = false;
                Advance();
            }
            return new(member.Lease, member.NextSequence, CatchUp(member.Lease.Token, afterRevision));
        }
    }

    // Called by the host timer, including while nobody is sending requests.
    public void Maintain()
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var member in members.Values.ToArray())
            {
                if (member.DisconnectedAt == null && now - member.LastSeen >= options.IdleTimeout)
                    member.DisconnectedAt = member.LastSeen + options.IdleTimeout;
                if (member.DisconnectedAt == null || now - member.DisconnectedAt < options.ReconnectGrace || member.TakenOver) continue;
                if (member.Lease.Seat == "") { members.Remove(member.Lease.Token); continue; }
                if (closed || game.Ended || game.Fault != null) continue;
                if (options.OnTimeout == DisconnectPolicy.EndSession) { closed = true; Advance(); return; }
                if (options.OnTimeout == DisconnectPolicy.Cpu)
                {
                    game.SetController(member.Lease.Seat, "cpu");
                    controllers[member.Lease.Seat] = "cpu";
                    member.TakenOver = true;
                    Advance();
                }
            }
            if (closed || game.Ended || !started || game.Fault != null) return;
            foreach (var timer in timerDue.Keys.ToArray())
            {
                if (now < timerDue[timer]) continue;
                timerDue[timer] = now + timer.Interval;
                var action = game.Actions(timer.Seat).FirstOrDefault(a => a.Id == timer.ActionId);
                if (action == null || controllers[timer.Seat] != "human") continue;
                var result = game.SubmitHuman(timer.Seat, action.Command);
                Advance(new(result.Sequence, timer.Seat, result.Accepted, result.Accepted ? "accepted" : "operation-rejected"));
            }
        }
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private MemberState Member(string token)
    {
        var member = members.GetValueOrDefault(token) ?? throw new OnlineException("invalid-identity");
        if (member.DisconnectedAt != null) throw new OnlineException("disconnected");
        member.LastSeen = time.GetUtcNow();
        return member;
    }
    private void Capture(PlayerEvent? entry = null)
    {
        // Query first: rule failures must appear consistently in every identity's frame.
        var actions = controllers.Keys.ToDictionary(s => s, s => started && !closed
            ? game.Actions(s).Where(a => !timerDue.Keys.Any(t => t.Seat == s && t.ActionId == a.Id)).ToArray() : []);
        foreach (var seat in controllers.Keys)
            history[seat].Add(Wire.Copy(new OnlineFrame(revision, game.View(seat), game.Fault == null ? actions[seat] : [], started, closed, game.Fault?.Code, entry)));
        history[""].Add(Wire.Copy(new OnlineFrame(revision, GameEngine.ProjectAudienceSnapshot(game.ExportAuthorityState()), [], started, closed, game.Fault?.Code, entry)));
    }
    private void Advance(PlayerEvent? entry = null)
    {
        revision++;
        if (revision >= options.MaxRevisions) closed = true;
        Capture(entry);
    }
    public IReadOnlyList<OnlineFrame> CatchUp(string token, long afterRevision)
    {
        lock (gate)
        {
            var member = Member(token);
            if (afterRevision < -1 || afterRevision > revision) throw new OnlineException("invalid-cursor");
            return Wire.Copy(history[member.Lease.Seat].Where(f => f.Revision > afterRevision).ToArray());
        }
    }
    public OnlineRecording ExportPlayerRecording(string token)
    {
        lock (gate)
        {
            var seat = Member(token).Lease.Seat;
            return new(1, Identity, seat, Wire.Copy(history[seat]));
        }
    }
    private void EnsureOpen() { if (closed) throw new OnlineException("session-closed"); }
    public void Close() { lock (gate) { if (!closed) { closed = true; Advance(); } } }
    public SessionRecording ExportAuthorityRecording() { lock (gate) return game.ExportRecording(); }
    public IReadOnlyList<Diagnostic> ExportAuthorityDiagnostics() { lock (gate) return game.ExportDiagnostics(); }
    public OnlineHostStatus Status { get { lock (gate) return new(revision, game.Ended, closed, game.Fault?.Code); } }
    public void Dispose() { lock (gate) { closed = true; game.Dispose(); } }
}
