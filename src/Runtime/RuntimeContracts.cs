using BoardGame.Kernel;

namespace BoardGame.Runtime;

public interface IDefinitionLoader
{
    GameDefinition Load(string directory);
    IReadOnlyList<string> ContentFiles(string directory);
    string Source { get; }
}

public interface IRuleProvider
{
    IGameRules Rules { get; }
    IGameRules Prepare(GameDefinition definition);
    void Apply(IGameRules rules);
}

public interface ICpuDecisionProvider
{
    string Entry { get; }
    Command Decide(SeatView view,IReadOnlyList<ActionOffer> actions,uint random);
    Action Prepare(GameDefinition definition);
}

public interface IEffectProvider
{
    Command Expand(SeatView view,Command command);
}

public interface IAuthoritySession
{
    SeatView Project(string seat);
    bool CanAct(string seat);
    IReadOnlyList<ActionOffer> Actions(string seat);
    CommandResult Submit(string seat,Command command);
    GameState ExportAuthorityState();
    ReplayLog ExportLog();
    IReadOnlyList<Diagnostic> ExportDiagnostics();
}

public interface IGameSession : IDisposable
{
    SessionFault? Fault { get; }
    int Steps { get; }
    bool Ended { get; }
    string CurrentSeat { get; }
    bool IsCpu { get; }
    bool SupportsCpu { get; }
    SeatView View(string seat);
    IReadOnlyList<ActionOffer> Actions(string? seat=null);
    CommandResult SubmitHuman(string seat,Command command);
    bool StepCpu();
    void SetController(string seat, string controller);
    ReplayLog ExportAuthorityLog();
    SessionRecording ExportRecording();
    IReadOnlyList<Diagnostic> ExportDiagnostics();
    GameState ExportAuthorityState();
    string AuthorityStateHash();
    void ReloadScripts(LoadedModule candidate);
}

public interface IRecordingProvider
{
    SessionRecording Capture();
    void RecordVersion(LoadedModule module);
    IAuthoritySession Replay(SessionRecording recording);
}
