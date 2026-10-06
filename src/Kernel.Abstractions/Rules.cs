namespace BoardGame.Kernel;
public sealed record ActionOffer(string Id,string Label,Command Command,string? Gesture=null,string? Target=null,List<string>? Shortcuts=null);
public sealed record RuleDecision(bool Allowed,string Reason,List<Command> Commands);
public interface IGameRules
{
    IReadOnlyList<ActionOffer> Actions(SeatView view);
    RuleDecision Decide(SeatView view,Command command);
}
public sealed record Diagnostic(long Sequence,string Actor,string Entry,string Message);
