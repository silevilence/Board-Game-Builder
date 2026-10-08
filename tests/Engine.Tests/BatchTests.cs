using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class BatchTests
{
    public static void Run()
    {
        var root=Path.Combine("artifacts","fault-tests",Guid.NewGuid().ToString());
        var m=TestModules.Load("samples/quick-cards");
        var faults=new Dictionary<string,LoadedModule> {
            ["cpu-failure"]=m with { Cpu="return {decide=function(c) error('injected') end}" },
            ["no-progress"]=m with { Rules="return {actions=function(c) return {{id='wait',label='Wait',command={kind='wait'}}} end, decide=function(c) return {allowed=true,reason='',commands={{kind='set',entity='match',property='rounds',value=3}}} end}" },
            ["no-actions"]=m with { Rules="return {actions=function(c) return {} end}" }
        };
        foreach(var fault in faults)
        {
            var modified=TestModules.Rebuild(fault.Value);
            var result=BatchRunner.Run(modified,123,1,Path.Combine(root,fault.Key)).Single();
            Check.That(!result.Success && result.Error==fault.Key && File.Exists(result.LogPath),Wire.Json(result));
            Check.That(File.Exists(Path.Combine(Path.GetDirectoryName(result.LogPath)!,"module.snapshot.json")));
            Check.That(result.ReplayEqual);
        }
        var limited=BatchRunner.Run(m,1,1,Path.Combine(root,"step-limit"),1).Single();
        Check.That(!limited.Success && limited.Error=="step-limit");
    }
}
