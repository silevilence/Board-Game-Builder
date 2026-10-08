using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;

public static class M2Acceptance
{
    public static void Run()
    {
        var results=new List<object>(); var root=Path.GetFullPath("artifacts/m2-mode-gate"); Directory.CreateDirectory(root);
        foreach(var name in new[]{"quick-cards","hex-skirmish"})
        {
            var module=GameRuntime.Load("samples/"+name); var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
            for(uint seed=1;seed<=20;seed++)
            {
                using IDebugSession local=new InProcessDebugSession(module,seed,controllers,"p1");
                using IDebugSession remote=ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",module,seed,controllers,"p1",true).Result;
                var a=local.SendAsync(new("frame")).Result; var b=remote.SendAsync(new("frame")).Result;
                var count=0;
                while(!a.Frame.View.Ended && a.Frame.Fault==null && count++<500)
                {
                    var command=new DebugRequest("cpu",a.Frame.Revision); a=local.SendAsync(command).Result; b=remote.SendAsync(command).Result;
                    Check.That(Wire.Json(a.Frame)==Wire.Json(b.Frame),$"{name}/{seed}: modes differ");
                }
                Check.That(a.Frame.View.Ended && a.Frame.Fault==null,$"{name}/{seed}: not ended");
                var first=local.SendAsync(new("recording")).Result.Recording!; var second=remote.SendAsync(new("recording")).Result.Recording!;
                Check.That(Wire.Json(first)==Wire.Json(second),$"{name}/{seed}: recording differs");
                Check.That(Wire.Hash(Wire.Json(second.Replay().ExportAuthorityState()))==a.Frame.StateHash);
                File.WriteAllText(Path.Combine(root,$"{name}-{seed}.json"),Wire.Json(second));
                results.Add(new {module=name,module.Fingerprint,seed,commands=count,success=true,modesEqual=true,replayEqual=true,stateHash=a.Frame.StateHash});
                File.WriteAllText(Path.Combine(root,"summary.json"),Wire.Json(results));
                Console.WriteLine($"GATE {name} seed={seed} commands={count}");
            }
        }
    }
}
