using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class RuntimeEntryTests
{
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-entry-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/quick-cards",directory);
            var module=GameRuntime.Load(directory);var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
            using IDebugSession local=new InProcessDebugSession(module,7,controllers,"p1");
            using IDebugSession remote=ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",module,7,controllers,"p1",true).GetAwaiter().GetResult();
            var a=local.SendAsync(new("frame")).Result;var b=remote.SendAsync(new("frame")).Result;
            while(!a.Frame.View.Ended && a.Frame.Fault==null)
            {
                a=local.SendAsync(new("cpu",a.Frame.Revision)).Result;b=remote.SendAsync(new("cpu",b.Frame.Revision)).Result;
                Check.That(Wire.Json(a)==Wire.Json(b),"Plugin-backed debug modes diverged");
            }
            Check.That(a.Frame.View.Ended && b.Frame.View.Ended);
            var batch=BatchRunner.Run(module,7,1,Path.Combine(directory,"batch"));
            Check.That(batch.Single().Success && batch.Single().StateHash==a.Frame.StateHash);
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
    }
}
