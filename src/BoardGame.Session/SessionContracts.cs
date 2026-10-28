using BoardGame.Kernel;
namespace BoardGame.Runtime;

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
    void CancelFlow();
    void ResumeFlow(CancellationToken cancellation=default);
    void SetController(string seat, string controller);
    ReplayLog ExportAuthorityLog();
    SessionRecording ExportRecording();
    IReadOnlyList<Diagnostic> ExportDiagnostics();
    GameState ExportAuthorityState();
    string AuthorityStateHash();
    void ReloadScripts(LoadedModule candidate);
}

