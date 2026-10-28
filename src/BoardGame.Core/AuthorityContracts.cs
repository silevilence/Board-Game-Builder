using System.Runtime.CompilerServices;
using BoardGame.Kernel;

[assembly:InternalsVisibleTo("Runtime")]
[assembly:InternalsVisibleTo("BoardGame.Hosting")]
[assembly:InternalsVisibleTo("BoardGame.Lua")]
[assembly:InternalsVisibleTo("BoardGame.Wasm")]
[assembly:InternalsVisibleTo("BoardGame.Session")]
[assembly:InternalsVisibleTo("BoardGame.Recording")]

namespace BoardGame.Runtime;

// This is an explicitly privileged host/author interface, never a rules or display input.
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

public interface ISeatProjection
{
    SeatView View();
}
public interface IBoardProjectionSource
{
    ISeatProjection ForSeat(string seat);
}
public interface IBoardCommandSource
{
    bool CanAct(string seat);
    IReadOnlyList<ActionOffer> Actions(string seat);
    CommandResult Submit(string seat,Command command);
}
internal interface IFlowAuthority
{
    CommandResult SubmitFlow(string? actor,Command command,long revision,Action<GameEngine.FlowTransaction> operation);
}
