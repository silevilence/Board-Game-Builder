using BoardGame.Kernel;
namespace BoardGame.Runtime;

internal sealed class EffectRules(IGameRules rules,IEffectProvider plugins) : IGameRules
{
    public IReadOnlyList<ActionOffer> Actions(SeatView view)=>rules.Actions(view);
    public RuleDecision Decide(SeatView view,Command command)
    {
        var decision=rules.Decide(view,command);
        if(!decision.Allowed) return decision;
        // Expansion is pure: the Kernel applies the entire resulting batch or none of it.
        return decision with {Commands=decision.Commands.Select(c=>plugins.Expand(view,c)).ToList()};
    }
}

