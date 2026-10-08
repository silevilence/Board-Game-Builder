using System.Security.Cryptography;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Networking;

public sealed record OnlineIdentity(int Protocol, string Fingerprint)
{
    public static OnlineIdentity For(LoadedModule module) => new(1, module.Fingerprint);
}
public sealed record SeatLease(string Token, string Seat);
public sealed record OnlineFrame(long Revision, SeatView View, IReadOnlyList<ActionOffer> Actions,
    bool Started, bool Closed, string? Fault);
public sealed record OnlineProposal(long Sequence, long ExpectedRevision, string Actor, Command Command);
public sealed record OnlineReceipt(long Sequence, long NextSequence, CommandResult Result, OnlineFrame Frame);
public sealed class OnlineException(string code) : Exception(code);

// Only the trusted host owns this object. Remote callers receive detached projections.
public sealed class OnlineSession : IDisposable
{
    private readonly object gate = new();
    private readonly IGameSession game;
    private readonly Dictionary<string, string> controllers;
    private sealed class MemberState(SeatLease lease)
    {
        public SeatLease Lease { get; } = lease;
        public long NextSequence { get; set; } = 1;
        public string? LastHash { get; set; }
        public OnlineReceipt? LastReceipt { get; set; }
    }
    private readonly Dictionary<string, MemberState> members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<OnlineFrame>> history = new(StringComparer.Ordinal);
    private bool started;
    private bool closed;
    private long revision;
    public OnlineIdentity Identity { get; }

    public OnlineSession(LoadedModule module, uint seed, IReadOnlyDictionary<string, string> controllers)
    {
        if (module.Initial.Seats.Any(s => !controllers.ContainsKey(s.Id)) ||
            controllers.Count != module.Initial.Seats.Count || controllers.Values.Any(v => v is not ("human" or "cpu")))
            throw new ArgumentException("Every seat requires a human or cpu controller.");
        Identity = OnlineIdentity.For(module);
        this.controllers = new(controllers);
        game = new PlaySession(module, seed, controllers);
        started = !controllers.Values.Contains("human");
        foreach (var seat in controllers.Keys) history.Add(seat, []);
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
            var lease = new SeatLease(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), seat);
            members.Add(lease.Token, new(lease));
            started = controllers.Where(p => p.Value == "human").All(p => members.Values.Any(m => m.Lease.Seat == p.Key));
            Advance();
            return lease;
        }
    }

    public OnlineFrame Read(string token)
    {
        lock (gate) return Wire.Copy(history[Member(token).Lease.Seat][^1]);
    }

    public OnlineReceipt Submit(string token, OnlineProposal proposal)
    {
        lock (gate)
        {
            EnsureOpen();
            var member = Member(token);
            if (proposal.Actor != member.Lease.Seat) throw new OnlineException("identity-mismatch");
            var hash = Wire.Hash(Wire.Json(proposal));
            if (proposal.Sequence == member.NextSequence - 1 && member.LastReceipt != null)
            {
                if (hash != member.LastHash) throw new OnlineException("sequence-conflict");
                return Wire.Copy(member.LastReceipt);
            }
            if (proposal.Sequence != member.NextSequence) throw new OnlineException("out-of-order");
            if (!started) throw new OnlineException("waiting-for-players");
            var result = proposal.ExpectedRevision == revision
                ? game.SubmitHuman(member.Lease.Seat, proposal.Command) : new CommandResult(0, false, "stale-revision");
            // Script diagnostics and commands belong only in the host recording.
            if (!result.Accepted && result.Reason != "stale-revision") result = result with { Reason = "operation-rejected" };
            Advance();
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
            Advance();
            return result;
        }
    }

    public void Leave(string token)
    {
        lock (gate)
        {
            _ = Member(token);
            members.Remove(token);
            closed = true; // Explicit leave ends the session; never silently substitutes a CPU.
            Advance();
        }
    }

    private OnlineFrame Frame(string seat) => new(revision, game.View(seat),
        started && !closed ? game.Actions(seat) : [], started, closed, game.Fault?.Code);
    private MemberState Member(string token) => members.GetValueOrDefault(token) ?? throw new OnlineException("invalid-identity");
    private void Capture()
    {
        // Query first: rule failures must appear consistently in every identity's frame.
        var actions = controllers.Keys.ToDictionary(s => s, s => started && !closed ? game.Actions(s) : []);
        foreach (var seat in controllers.Keys)
            history[seat].Add(Wire.Copy(new OnlineFrame(revision, game.View(seat), game.Fault == null ? actions[seat] : [], started, closed, game.Fault?.Code)));
    }
    private void Advance() { revision++; Capture(); }
    public IReadOnlyList<OnlineFrame> CatchUp(string token, long afterRevision)
    {
        lock (gate)
        {
            var member = Member(token);
            if (afterRevision < -1 || afterRevision > revision) throw new OnlineException("invalid-cursor");
            return Wire.Copy(history[member.Lease.Seat].Where(f => f.Revision > afterRevision).ToArray());
        }
    }
    private void EnsureOpen() { if (closed) throw new OnlineException("session-closed"); }
    public SessionRecording ExportAuthorityRecording() { lock (gate) return game.ExportRecording(); }
    public void Dispose() { lock (gate) { closed = true; game.Dispose(); } }
}
