using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record RunSummary(uint Seed, int Steps, bool Ended, string Reason, string StateHash, bool RepeatMatches, bool ReplayMatches, SessionFault? Fault, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record RunResult(ProjectSource Source, RunSummary[] Runs) : IAuthorOutcome
{
    public bool Success => Runs.All(r => r.Ended && r.Reason == "ended" && r.RepeatMatches && r.ReplayMatches);
    public int ExitCode => Success ? 0 : 1;
}
public sealed record DebugStep(string RequestId, string Seat, string Action, Command Command);
public sealed record DebugDocument(string Id, ProjectSource Source, LoadedModule Module, uint Seed, DebugStep[] Steps,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SessionRecording? Recording=null);

public static class RunOperations
{
    private static AuthorOperationInfo Info(string id, string description, Dictionary<string, AuthorParameter> parameters, string[] effects) =>
        new(id, "author.run", description, parameters, new() { ["type"] = "object", ["description"] = "Source version, explicit session identity or per-seed results; authority data is for author debugging only" }, [], effects);
    private static AuthorOperationInfo CpuInfo => Info("run.cpu", "Bounded all-CPU runs, repeat determinism and authoritative replay verification",
        new() { ["seed"] = new("integer", false, "First seed (default 1)"), ["count"] = new("integer", false, "Runs, 1..100 (default 1)"), ["limit"] = new("integer", false, "Maximum steps, 1..100000 (default 500)") }, []);
    private static AuthorOperationInfo StartInfo => Info("debug.start", "Create an explicit immutable-input debug session artifact; all seats are controlled by author actions",
        new() { ["target"] = new("string", true, "New session file outside the project"), ["seed"] = new("integer", false, "Seed (default 1)") }, ["artifact"]);
    private static AuthorOperationInfo InspectInfo => Info("debug.inspect", "Inspect the frozen session, legal actions and seat projection; authority=true explicitly requests full debug truth",
        new() { ["session"] = new("string", true, "Session file"), ["seat"] = new("string", false, "Projection seat, default current"), ["authority"] = new("boolean", false, "Include authoritative state") }, []);
    private static AuthorOperationInfo StepInfo => Info("debug.step", "Execute a legal action against the specified debug step; use a stable --request for retry",
        new() { ["session"] = new("string", true, "Session file"), ["step"] = new("integer", true, "Expected step number"), ["action"] = new("string", true, "Offered action ID"), ["seat"] = new("string", false, "Acting seat, default current") }, ["artifact"]);
    private static AuthorOperationInfo RecordInfo => Info("debug.record", "Write a recording and verify replay against the debug session",
        new() { ["session"] = new("string", true, "Session file"), ["target"] = new("string", true, "New recording output") }, ["artifact"]);
    public static AuthorPlugin Plugin => new("author.run", "1", 1, [], ["author.operations"], scope =>
    {
        scope.Register(new(CpuInfo, Cpu)); scope.Register(new(StartInfo, Start)); scope.Register(new(InspectInfo, Inspect));
        scope.Register(new(StepInfo, Step)); scope.Register(new(RecordInfo, Record));
    }, "Headless runtime sessions and deterministic verification") { Documents=[AuthorDocuments.Bundled("lua","author.run","Lua 规则和 CPU API"),AuthorDocuments.Bundled("debugging","author.run","校验与调试")], Operations = [CpuInfo, StartInfo, InspectInfo, StepInfo, RecordInfo] };
    private static Task<object> Cpu(AuthorCall call)
    {
        var seed = call.Integer("seed", 1); var count = call.Integer("count", 1); var limit = call.Integer("limit", 500);
        if (seed < 0 || count is < 1 or > 100 || limit is < 1 or > 100000) throw new AuthorOperationException("arguments.invalid", "Invalid seed, count or limit");
        using var snapshot = new ProjectSnapshot(call.View);
        var module = GameRuntime.Load(snapshot.Directory);
        var controllers = module.Initial.Seats.ToDictionary(s => s.Id, _ => "cpu");
        var runs = new List<RunSummary>();
        for (var index = 0; index < count; index++)
        {
            call.Cancellation.ThrowIfCancellationRequested();
            var currentSeed = checked((uint)seed + (uint)index);
            using var first = Run(currentSeed); using var second = Run(currentSeed);
            var replay = first.ExportRecording().Replay();
            var reason = first.Fault?.Code ?? (first.Ended ? "ended" : "stopped");
            runs.Add(new(currentSeed, first.Steps, first.Ended, reason, first.AuthorityStateHash(),
                first.AuthorityStateHash() == second.AuthorityStateHash() && first.Fault == second.Fault && first.Steps == second.Steps,
                Wire.Json(replay.ExportAuthorityState()) == Wire.Json(first.ExportAuthorityState()), first.Fault, first.ExportDiagnostics()));
        }
        return Task.FromResult<object>(new RunResult(call.View.Source, runs.ToArray()));
        PlaySession Run(uint currentSeed)
        {
            var game = new PlaySession(module, currentSeed, controllers, limit,call.Cancellation);
            try
            {
                while (!game.Ended && game.Fault == null) { call.Cancellation.ThrowIfCancellationRequested(); if (!game.StepCpu()) break; }
                return game;
            }
            catch { game.Dispose(); throw; }
        }
    }
    private static Task<object> Start(AuthorCall call)
    {
        var seed = call.Integer("seed", 1); if (seed < 0) throw new AuthorOperationException("arguments.invalid", "Seed must be nonnegative");
        using var snapshot = new ProjectSnapshot(call.View);
        var module = GameRuntime.Load(snapshot.Directory);
        var document = new DebugDocument(Guid.NewGuid().ToString("N"), call.View.Source, module, (uint)seed, []);
        if(module.Initial.Flow!=null)
        {
            using var game=new PlaySession(module,(uint)seed,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"),cancellation:call.Cancellation);
            document=document with {Recording=game.ExportRecording()};
        }
        var target = Path.GetFullPath(call.String("target"));
        AtomicTextFile.Write(target, Wire.Json(document), false);
        return Task.FromResult<object>(new { document.Id, document.Source, target, step = 0 });
    }
    private static DebugDocument Read(AuthorCall call) => Wire.Read<DebugDocument>(File.ReadAllText(Path.GetFullPath(call.String("session"))));
    private static PlaySession Rebuild(DebugDocument document, CancellationToken cancellation)
    {
        if(document.Module.Initial.Flow!=null)
            return PlaySession.Restore(document.Module,document.Recording ?? throw new AuthorOperationException("debug.corrupt","Missing flow recording"),
                document.Module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"),cancellation:cancellation);
        var game = new PlaySession(document.Module, document.Seed, document.Module.Initial.Seats.ToDictionary(s => s.Id, _ => "human"));
        try
        {
            foreach (var step in document.Steps)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!game.SubmitHuman(step.Seat, step.Command).Accepted) throw new AuthorOperationException("debug.corrupt", "Stored session action no longer replays");
            }
            return game;
        }
        catch { game.Dispose(); throw; }
    }
    private static Task<object> Inspect(AuthorCall call)
    {
        var document = Read(call); using var game = Rebuild(document, call.Cancellation);
        var seat = call.OptionalString("seat") ?? game.CurrentSeat;
        var actions = game.Actions(seat);var view = game.View(seat);
        return Task.FromResult<object>(new { document.Id, document.Source, latestProject = call.View.Source,
            step = game.Steps, game.Ended, game.Fault, diagnostics = game.ExportDiagnostics(), actions, view,
            authority = call.Arguments.TryGetProperty("authority", out var full) && full.GetBoolean() ? game.ExportAuthorityState() : null });
    }
    private static Task<object> Step(AuthorCall call)
    {
        var target = Path.GetFullPath(call.String("session"));
        using var gate = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var document = Read(call); using var game = Rebuild(document, call.Cancellation);
        var repeated = document.Steps.SingleOrDefault(s => s.RequestId == call.RequestId);
        if (repeated != null)
        {
            if (Array.IndexOf(document.Steps, repeated) != call.Integer("step", -1) || repeated.Action != call.String("action") || call.OptionalString("seat") is { } actor && actor != repeated.Seat)
                throw new AuthorOperationException("request.mismatch", "Debug request already identifies another action");
            return Task.FromResult<object>(new { document.Id, document.Source, step = game.Steps, repeated = true });
        }
        if (call.Integer("step", -1) != game.Steps) throw new AuthorOperationException("debug.conflict", "Debug session step changed");
        var seat = call.OptionalString("seat") ?? game.CurrentSeat;
        var action = game.Actions(seat).SingleOrDefault(a => a.Id == call.String("action")) ?? throw new AuthorOperationException("debug.action", "Action is not offered to this seat");
        var result = game.SubmitHuman(seat, action.Command);
        if (!result.Accepted) throw new AuthorOperationException("debug.rejected", result.Reason);
        document = document with { Steps = [..document.Steps, new(call.RequestId, seat, action.Id, action.Command)],
            Recording=document.Module.Initial.Flow==null?null:game.ExportRecording() };
        AtomicTextFile.Write(target, Wire.Json(document), true);
        return Task.FromResult<object>(new { document.Id, document.Source, step = game.Steps, result, game.Ended, game.Fault });
    }
    private static Task<object> Record(AuthorCall call)
    {
        var document = Read(call); using var game = Rebuild(document, call.Cancellation);
        var recording = game.ExportRecording(); var replay = recording.Replay();
        if (Wire.Json(replay.ExportAuthorityState()) != Wire.Json(game.ExportAuthorityState())) throw new AuthorOperationException("replay.mismatch", "Authoritative replay differs");
        var target = Path.GetFullPath(call.String("target")); AtomicTextFile.Write(target, Wire.Json(recording), false);
        return Task.FromResult<object>(new { document.Id, document.Source, target, replayMatches = true, stateHash = game.AuthorityStateHash() });
    }
}
