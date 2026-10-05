using BoardGame.Kernel;
using BoardGame.Lua;
namespace Engine.Tests;
public static class LuaTests
{
    public static void Run()
    {
        const string source="""
        return {
          actions=function(ctx) return {{id="play",label="Play",command={kind="play",entity="card"}}} end,
          decide=function(ctx)
            if ctx.command.kind ~= "play" then return {allowed=false,reason="illegal play",commands={}} end
            ctx.view.entities[1].properties.value=999
            return {allowed=true,reason="",commands={
              {kind="set",entity="card",property="value",value=9},
              {kind="finish",winners={ctx.view.seat}}
            }}
          end
        }
        """;
        var initial=CommandTests.Initial();var rules=new LuaRules(source,"test.lua");
        var e=new GameEngine(initial,rules:rules);
        Check.That(e.Actions("p1").Single().Id=="play");
        Check.That(!e.Submit("p1",new() { Kind="bad" }).Accepted);
        Check.That(e.ExportAuthorityState().Entities["card"].Properties["value"]==3);
        Check.That(e.Submit("p1",new() { Kind="play",Entity="card" }).Accepted);
        Check.That(e.Project("p1").Ended && e.ExportAuthorityState().Entities["card"].Properties["value"]==9);
        var module=new LoadedModule("test","1","development",initial,source,"","test.lua","");
        Check.That(Wire.Json(GameEngine.Replay(module,e.ExportLog()).ExportAuthorityState())==Wire.Json(e.ExportAuthorityState()));
        var bad=new GameEngine(initial,rules:new LuaRules(source.Replace("kind=\"finish\"","kind='transfer',entity='card',target='absent'"),"bad.lua"));
        Check.That(!bad.Submit("p1",new() { Kind="play" }).Accepted);
        Check.That(bad.ExportAuthorityState().Entities["card"].Properties["value"]==3,"partial derived command committed");
        var hidden=new GameEngine(VisibilityTests.Initial(),rules:new LuaRules(source,"test.lua"));
        Check.That(!hidden.Submit("p1",new() { Kind="play" }).Accepted,"rule accessed hidden card");
    }
}
