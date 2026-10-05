using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class DebugTests
{
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
