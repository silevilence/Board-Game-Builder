using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class DebugTests
{
    public static void Observers()
    {
        var module=DirectoryModule.Load("samples/quick-cards"); var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
        using var a=new DebugSession(module,42,controllers,"p1"); using var b=new DebugSession(module,42,controllers,"p1");
        var left=a.Handle(new("cpu",0)); var right=b.Handle(new("cpu",0));
        Check.That(left.AuthorityState==null && left.Frame.View.Entities.All(e=>e.Container!="hand-p2"));
        left=a.Handle(new("observe",Seat:"p2",Authority:true)); Check.That(left.AuthorityState!=null && left.Frame.View.Entities.Any(e=>e.Container=="hand-p2") && left.Frame.View.Entities.All(e=>e.Container!="hand-p1"));
        while(!left.Frame.View.Ended)
        {
            left=a.Handle(new("cpu",left.Frame.Revision)); right=b.Handle(new("cpu",right.Frame.Revision)); Check.That(left.Frame.StateHash==right.Frame.StateHash,"Debug visibility affected CPU");
        }
        left=a.Handle(new("observe",Seat:"p1")); Check.That(left.AuthorityState==null);
    }
    public static void Timeline()
    {
        var module=DirectoryModule.Load("samples/quick-cards"); var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller);
        foreach(var external in new[]{false,true})
        {
            using IDebugSession client=external?ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",module,9,controllers,"p1",true).Result:new InProcessDebugSession(module,9,controllers,"p1");
            var reply=client.SendAsync(new("frame")).Result;
            reply=client.SendAsync(new("submit",reply.Frame.Revision,new(){Kind="invalid"})).Result; Check.That(reply.Result?.Accepted==false && reply.Timeline!.Count==1);
            reply=client.SendAsync(new("submit",reply.Frame.Revision,reply.Frame.Actions[0].Command)).Result;
            reply=client.SendAsync(new("submit",reply.Frame.Revision,reply.Frame.Actions[0].Command)).Result;
            Check.That(reply.Frame.Cpu); reply=client.SendAsync(new("pause")).Result;
            var count=reply.Timeline!.Count; reply=client.SendAsync(new("cpu",reply.Frame.Revision)).Result; Check.That(reply.Timeline!.Count==count);
            reply=client.SendAsync(new("step",reply.Frame.Revision)).Result; Check.That(reply.Timeline!.Count==count+1 && reply.Diagnostics!.Any(d=>d.Entry.EndsWith(":decide")));
            var state=reply.Frame.StateHash; reply=client.SendAsync(new("replay")).Result; Check.That(reply.Frame.IsReplay && reply.Frame.StateHash==state);
        }
    }
    public static void External()
    {
        var module=DirectoryModule.Load("samples/quick-cards"); var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
        using IDebugSession local=new InProcessDebugSession(module,7,controllers,"p1");
        using IDebugSession remote=ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",module,7,controllers,"p1",true).GetAwaiter().GetResult();
        var a=local.SendAsync(new("frame")).Result; var b=remote.SendAsync(new("frame")).Result;
        var bad=module with {Rules="broken lua"}; bad=bad with {Fingerprint=ModuleSnapshot.Fingerprint(bad)};
        Check.Throws(()=>remote.SendAsync(new("reload",Module:bad)).GetAwaiter().GetResult());
        Check.That(remote.SendAsync(new("frame")).Result.Frame.StateHash==b.Frame.StateHash,"Failed reload killed Player");
        while(!a.Frame.View.Ended)
        {
            var request=new DebugRequest("cpu",a.Frame.Revision); a=local.SendAsync(request).Result; b=remote.SendAsync(request).Result;
            Check.That(Wire.Json(a)==Wire.Json(b),"Debug modes diverged");
        }
        a=local.SendAsync(new("recording")).Result; b=remote.SendAsync(new("recording")).Result; Check.That(Wire.Json(a.Recording)==Wire.Json(b.Recording));
        remote.Dispose(); Check.Throws(()=>remote.SendAsync(new("frame")).GetAwaiter().GetResult(),"Player");
    }
    public static void InProcess()
    {
        var module=DirectoryModule.Load("samples/quick-cards"); var initial=Wire.Json(module.Initial); string? previous=null;
        for(var cycle=0;cycle<3;cycle++)
        {
            using IDebugSession session=new InProcessDebugSession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller),"p1");
            var reply=session.SendAsync(new("frame")).GetAwaiter().GetResult();
            while(!reply.Frame.View.Ended && reply.Frame.Fault==null)
                reply=session.SendAsync(reply.Frame.Cpu?new("cpu",reply.Frame.Revision):new("submit",reply.Frame.Revision,reply.Frame.Actions[0].Command)).GetAwaiter().GetResult();
            Check.That(reply.Frame.View.Ended && reply.Frame.Fault==null);
            Check.That(previous==null || previous==reply.Frame.StateHash); previous=reply.Frame.StateHash;
            Check.That(initial==Wire.Json(module.Initial));
        }
    }
}
