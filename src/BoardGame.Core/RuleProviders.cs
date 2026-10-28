using BoardGame.Kernel;
namespace BoardGame.Runtime;

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

