using BoardGame.Kernel;
namespace Engine.Tests;
public static class TurnTests
{
    private sealed class TurnRules : IGameRules
    {
        public IReadOnlyList<ActionOffer> Actions(SeatView v) => [new("pass","Pass",new() { Kind="next",Phase="play" })];
        public RuleDecision Decide(SeatView v,Command c) => new(true,"",[c]);
    }
    public static void Run()
    {
        var s=CommandTests.Initial();s.Phase="setup";var e=new GameEngine(s,rules:new TurnRules());
        Check.That(e.Actions("p2").Count==0 && e.Actions("p1").Count==1);
        Check.That(!e.Submit("p2",new() { Kind="next" }).Accepted);
        Check.That(e.Submit("p1",e.Actions("p1")[0].Command).Accepted);
        Check.That(e.Project("p2").Phase=="play" && e.Project("p2").CurrentSeat=="p2");
        Check.That(e.Submit("p2",e.Actions("p2")[0].Command).Accepted);
        Check.That(e.Project("p1").Turn==2 && e.Actions("p1").Count==1);
        var before=Wire.Json(e.ExportAuthorityState());Check.That(!e.Submit("p1",new() { Kind="phase",Phase="missing" }).Accepted);
        Check.That(before==Wire.Json(e.ExportAuthorityState()));
    }
}
