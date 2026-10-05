using BoardGame.Kernel;
using BoardGame.Lua;
namespace Engine.Tests;
public static class SandboxTests
{
    public static void Run()
    {
        var cases=new[] {
            "error('injected error')", "while true do end", "io.open('secrets')", "os.execute('echo fail')",
            "return require('socket')", "return math.random()", "return luanet.import_type('System.IO.File')",
            "return string.rep('x',1000000000)",
            "local function f() return f() end return f()",
            "local t={} t.self=t return t"
        };
        foreach(var body in cases)
        {
            var source="return {decide=function(ctx) "+body+" end}";
            var e=new GameEngine(CommandTests.Initial(),rules:new LuaRules(source,"fault.lua"));
            var before=Wire.Json(e.ExportAuthorityState());var result=e.Submit("p1",new() { Kind="play" });
            Check.That(!result.Accepted && before==Wire.Json(e.ExportAuthorityState()),body);
            Check.That(e.ExportDiagnostics().Any(d=>d.Message.Contains("fault.lua")),body);
            Check.That(e.ExportLog().Entries.Count==1);
        }
        Check.Throws(()=>LuaWorker.Invoke("while true do end","load.lua","decide","{}"),"load.lua");
        var many="return {decide=function(ctx) local c={} for i=1,65 do c[i]={kind='next'} end return {allowed=true,reason='',commands=c} end}";
        var excessive=new GameEngine(CommandTests.Initial(),rules:new LuaRules(many,"many.lua"));
        Check.That(excessive.Submit("p1",new() { Kind="play" }).Reason=="rule-command-limit");
        LuaTests.Run();
    }
}
