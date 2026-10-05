using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class DebugTests
{
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
