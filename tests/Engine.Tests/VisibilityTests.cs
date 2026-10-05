using BoardGame.Kernel;
namespace Engine.Tests;
public static class VisibilityTests
{
    public static GameState Initial()
    {
        var s=CommandTests.Initial();
        s.Entities["card"].Container="deck";s.Entities["card"].Owner=null;
        s.Entities["card"].Faces=["Secret ace"];s.Entities["card"].Visibility="hidden";
        s.Entities.Add("deck",new() { Id="deck",IsContainer=true,Ordered=true,Capacity=9,Visibility="hidden",Contents=["card"] });
        s.Entities.Add("hand1",new() { Id="hand1",IsContainer=true,Capacity=9,Visibility="owner",Owner="p1" });
        s.Entities.Add("hand2",new() { Id="hand2",IsContainer=true,Capacity=9,Visibility="owner",Owner="p2" });
        return s;
    }
    public static void Run()
    {
        var e=new GameEngine(Initial());
        Check.That(!Wire.Json(e.Project("p1")).Contains("Secret ace"));
        Check.That(e.Submit("p1",new() { Kind="draw",Entity="deck",Target="hand1" }).Accepted);
        Check.That(Wire.Json(e.Project("p1")).Contains("Secret ace"));
        Check.That(!Wire.Json(e.Project("p2")).Contains("Secret ace"));
        var hidden=e.Submit("p2",new() { Kind="flip",Entity="card" });
        var missing=e.Submit("p2",new() { Kind="flip",Entity="nonexistent" });
        Check.That(hidden.Reason==missing.Reason);
        Check.That(!Wire.Json(e.PlayerLog("p2")).Contains("card"));
        Check.That(e.Submit("p1",new() { Kind="transfer",Entity="card",Target="box" }).Accepted);
        Check.That(e.Submit("p1",new() { Kind="reveal",Entity="card",Visibility="public" }).Accepted);
        Check.That(Wire.Json(e.Project("p2")).Contains("Secret ace"));
        var flipped=Initial();flipped.Entities["card"].Container=null;flipped.Entities["deck"].Contents.Clear();
        flipped.Entities["card"].Visibility="public";flipped.Entities["card"].FaceUp=false;
        Check.That(!Wire.Json(new GameEngine(flipped).Project("p1")).Contains("Secret ace"));
        Check.Throws(()=>e.Project("unknown"));
    }
}
