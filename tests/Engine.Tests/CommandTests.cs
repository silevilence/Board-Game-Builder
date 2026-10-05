using BoardGame.Kernel;
namespace Engine.Tests;
public static class CommandTests
{
    public static GameState Initial()
    {
        var s = new GameState { Seats = [new("p1","a"),new("p2","b")], Phases = ["setup","play"], Phase = "play" };
        s.Entities.Add("card", new() { Id="card", Owner="p1", Faces=["Ace"], Properties=new() { ["value"]=3 } });
        s.Entities.Add("box", new() { Id="box", IsContainer=true, Capacity=1 });
        s.Entities.Add("other", new() { Id="other", Owner="p2" });
        return s;
    }
    public static void Run()
    {
        var initial = Initial(); var engine = new GameEngine(initial); initial.Entities.Clear();
        Check.That(engine.Submit("p1",new() { Kind="move", Entity="card",X=2,Y=4 }).Accepted);
        Check.That(engine.Submit("p1",new() { Kind="flip", Entity="card" }).Accepted);
        Check.That(engine.Submit("p1",new() { Kind="set", Entity="card",Property="value",Value=7 }).Accepted);
        Check.That(engine.Submit("p1",new() { Kind="transfer",Entity="card",Target="box" }).Accepted);
        var before = Wire.Json(engine.ExportAuthorityState());
        foreach (var c in new Command[] { new() { Kind="transfer",Entity="box",Target="box" },new() { Kind="transfer",Entity="missing",Target="box" },
            new() { Kind="move",Entity="other" },new() { Kind="transfer",Entity="box",Target="card" } })
            Check.That(!engine.Submit("p1",c).Accepted);
        Check.That(before == Wire.Json(engine.ExportAuthorityState()), "rejected command mutated state");
        var copy = engine.ExportAuthorityState(); copy.Entities.Clear(); Check.That(before == Wire.Json(engine.ExportAuthorityState()));
        Check.That(engine.ExportAuthoritySubmissions().Select(s=>s.Sequence).SequenceEqual(Enumerable.Range(1,8).Select(i=>(long)i)));
        var full=Initial(); full.Entities.Add("second",new() { Id="second",Owner="p1" }); var e=new GameEngine(full);
        e.Submit("p1",new() { Kind="transfer",Entity="card",Target="box" });
        Check.That(!e.Submit("p1",new() { Kind="transfer",Entity="second",Target="box" }).Accepted);
    }
}
