using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Lua;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class ReviewTests
{
    public static void Run()
    {
        var module=TestModules.Load("samples/quick-cards");
        Check.That(TestModules.Load("samples/quick-cards/").Fingerprint==module.Fingerprint);
        var engine=new GameEngine(CommandTests.Initial());var before=Wire.Json(engine.ExportAuthorityState());
        Check.That(!engine.Submit("p1",new() { Kind="move",Entity="card",X=double.NaN }).Accepted);
        Check.That(before==Wire.Json(engine.ExportAuthorityState()));
        engine.Submit("p1",new() { Kind="set",Entity="card",Property="value",Value=8 });
        var log=engine.ExportLog();log.Entries.RemoveAt(log.Entries.Count-1);
        var dev=new LoadedModule("test","1","development",CommandTests.Initial(),"","","","");
        Check.Throws(()=>GameEngine.Replay(dev,log),"footer");
        var source="return {decide=function(c) return {allowed=true,reason='',commands={{kind='finish',winners={}},{kind='set',entity='card',property='value',value=8}}} end}";
        var ended=new GameEngine(CommandTests.Initial(),rules:new LuaRules(source,"after-end.lua"));
        Check.That(!ended.Submit("p1",new() { Kind="play" }).Accepted);
        Check.That(!ended.Project("p1").Ended);
        using var invalidActions=new PlaySession(module with { Rules="return {actions=function(c) return {{id='bad',label='Bad'}} end}" });
        Check.That(invalidActions.Actions().Count==0 && invalidActions.Fault?.Code=="rule-failure");
        var dir=Path.Combine("artifacts","review-tests");Directory.CreateDirectory(dir);var path=Path.Combine(dir,"snapshot.json");
        File.WriteAllText(path,Wire.Json(module));Check.That(ModuleSnapshot.Load(path).Fingerprint==module.Fingerprint);
        File.WriteAllText(path,Wire.Json(module with { Cpu="changed" }));Check.Throws(()=>ModuleSnapshot.Load(path),"fingerprint");
        var broken=Wire.Copy(module);broken.Initial.Entities["deck"].Contents.Add("missing");File.WriteAllText(path,Wire.Json(broken));Check.Throws(()=>ModuleSnapshot.Load(path),"invalid");
        var randomSource="return {decide=function(c) return {allowed=true,reason='',commands={{kind='set',entity='card',property='value',value=random_index(1000000)}}} end}";
        int RandomValue(uint seed)
        {
            var e=new GameEngine(CommandTests.Initial(),seed,rules:new LuaRules(randomSource,"random.lua",seed));
            Check.That(e.Submit("p1",new() { Kind="roll" }).Accepted);return e.ExportAuthorityState().Entities["card"].Properties["value"];
        }
        for(var i=0;i<30;i++) Check.That(LuaWorker.Invoke("return {decide=function(c) return 7 end}","short-lived.lua","decide","{}")=="7");
        Check.That(RandomValue(1)==RandomValue(1));Check.That(RandomValue(1)!=RandomValue(2));
    }
}
