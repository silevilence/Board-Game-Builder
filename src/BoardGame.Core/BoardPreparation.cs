namespace BoardGame.Runtime;

/// <summary>Privileged detached state for preparing a board update. The owner serializes
/// Begin and TryCommit with direct authority mutations. Stale branches cannot publish.</summary>
public interface IBoardTransactionSource
{
    IBoardStateBranch Begin();
}
public interface IBoardStateBranch
{
    IAuthoritySession Authority {get;}
    bool TryCommit();
}
public interface IPreparedFlow : IDisposable
{
    IFlowProgression Value {get;}
    Action Seal();
}
