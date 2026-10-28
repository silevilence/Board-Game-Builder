namespace BoardGame.Runtime;

public sealed record SessionFault(string Code,string Seat,string Detail);

/// <summary>Privileged board progression capability. The owning coordinator serializes
/// Advance, Submit and Resume; only Cancel may run concurrently. Reading projections
/// never advances nodes. This contract does not depend on a session or update driver.</summary>
public interface IFlowProgression : IAuthoritySession
{
    SessionFault? Fault {get;}
    void Advance(CancellationToken cancellation=default);
    void Cancel();
    void Resume(CancellationToken cancellation=default);
    IPreparedFlow Prepare(IBoardStateBranch branch,CancellationToken cancellation=default);
}
