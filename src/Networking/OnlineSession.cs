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
public sealed class OnlineException(string code) : Exception(code);

// Only the trusted host owns this object. Remote callers receive detached projections.
public sealed class OnlineSession : IDisposable
{
    private readonly object gate = new();
    private readonly IGameSession game;
    private readonly Dictionary<string, string> controllers;
    private readonly Dictionary<string, SeatLease> members = new(StringComparer.Ordinal);
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
    }

    public SeatLease Join(OnlineIdentity identity, string? requestedSeat = null)
    {
        lock (gate)
        {
            EnsureOpen();
            if (identity != Identity) throw new OnlineException("version-mismatch");
            var seat = controllers.Keys.FirstOrDefault(s => controllers[s] == "human" &&
                (requestedSeat == null || s == requestedSeat) && !members.Values.Any(m => m.Seat == s));
            if (seat == null) throw new OnlineException("seat-unavailable");
            var lease = new SeatLease(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), seat);
            members.Add(lease.Token, lease);
            started = controllers.Where(p => p.Value == "human").All(p => members.Values.Any(m => m.Seat == p.Key));
            return lease;
        }
    }

    public OnlineFrame Read(string token)
    {
        lock (gate) return Frame(Member(token).Seat);
    }

    public CommandResult Submit(string token, string actor, Command command)
    {
        lock (gate)
        {
            EnsureOpen();
            var member = Member(token);
            if (member.Seat != actor) throw new OnlineException("identity-mismatch");
            if (!started) throw new OnlineException("waiting-for-players");
            var result = game.SubmitHuman(member.Seat, command);
            revision++;
            return result;
        }
    }

    public bool StepCpu()
    {
        lock (gate)
        {
            if (closed || !started || !game.IsCpu || game.Ended || game.Fault != null) return false;
            var result = game.StepCpu();
            revision++;
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
        }
    }

    private OnlineFrame Frame(string seat) => new(revision, game.View(seat),
        started && !closed ? game.Actions(seat) : [], started, closed, game.Fault?.Code);
    private SeatLease Member(string token) => members.GetValueOrDefault(token) ?? throw new OnlineException("invalid-identity");
    private void EnsureOpen() { if (closed) throw new OnlineException("session-closed"); }
    public SessionRecording ExportAuthorityRecording() { lock (gate) return game.ExportRecording(); }
    public void Dispose() { lock (gate) { closed = true; game.Dispose(); } }
}
