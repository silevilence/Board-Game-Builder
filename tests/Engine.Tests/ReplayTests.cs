using BoardGame.Kernel;
namespace Engine.Tests;
public static class ReplayTests
{
    public static void Run()
    {
        var s=VisibilityTests.Initial();
        for(var i=0;i<9;i++) { var id="c"+i;s.Entities.Add(id,new() { Id=id,Container="deck",Visibility="hidden",Faces=[id] });s.Entities["deck"].Contents.Add(id); }
        s.Entities["deck"].Capacity=10;
        var m=new LoadedModule("test","1","fingerprint",s,"","","rules","cpu");
        GameEngine Play() { var e=new GameEngine(s,42,m.Fingerprint);e.Submit("p1",new() { Kind="shuffle",Entity="deck" });e.Submit("p1",new() { Kind="draw",Entity="deck",Target="hand1" });e.Submit("p2",new() { Kind="move",Entity="missing" });return e; }
        var a=Play();var b=Play();Check.That(Wire.Json(a.ExportLog())==Wire.Json(b.ExportLog()));
        var replay=GameEngine.Replay(m,Wire.Read<ReplayLog>(Wire.Json(a.ExportLog())));
        Check.That(Wire.Json(a.ExportAuthorityState())==Wire.Json(replay.ExportAuthorityState()));
        foreach(var seat in s.Seats) Check.That(Wire.Json(a.Project(seat.Id))==Wire.Json(replay.Project(seat.Id)));
        var log=a.ExportLog();log.Entries[0].Effects[0].Order!.Reverse();Check.Throws(()=>GameEngine.Replay(m,log),"checksum");
        Check.Throws(()=>GameEngine.Replay(m with { Fingerprint="changed" },a.ExportLog()),"version mismatch");
        var broken=a.ExportLog();broken.Initial.Entities.Clear();Check.Throws(()=>GameEngine.Replay(m,broken),"initial state");
    }
}
