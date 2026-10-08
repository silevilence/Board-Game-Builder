using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public interface IGameAuthorFormat
{
    string DefinitionPath {get;}
    JsonObject Read(ModuleDocument document);
}

public static class GameAuthorFormats
{
    private sealed record ModuleJson(string DefinitionPath) : IGameAuthorFormat
    {public JsonObject Read(ModuleDocument document)=>JsonNode.Parse(document.Text(DefinitionPath))!.AsObject();}
    public static IGameAuthorFormat? Resolve(ModuleDocument document)
    {
        var plugins=document.Startup["plugins"]!.AsArray();
        var selection=plugins.SingleOrDefault(p=>p!["id"]!.GetValue<string>()=="definition.module");
        return selection==null?null:new ModuleJson(selection["configuration"]!["path"]!.GetValue<string>());
    }
    public static Dictionary<string,byte[]> Template()
    {
        RuntimePluginSelection Select(string id,object config)=>new(id,"1",1,JsonSerializer.SerializeToElement(config,Wire.Options));
        var startup=new RuntimeStartup(1,"untitled","1.0.0",
            [Select("definition.module",new {path="module.json"}),Select("kernel",new {}),Select("lua.rules",new {file="rules.lua"}),
                Select("lua.cpu",new {file="cpu.lua"}),Select("session.local",new {}),Select("recording",new {})]);
        return new(StringComparer.Ordinal)
        {
            [GameRuntime.Entry]=Encoding.UTF8.GetBytes(Wire.Json(startup)),
            ["module.json"]=Encoding.UTF8.GetBytes("""
            {"format":1,"resources":{},"seats":[{"id":"p1","team":"one","controller":"human"}],"phases":["play"],"entities":[]}
            """),
            ["rules.lua"]=Encoding.UTF8.GetBytes("return { actions=function(v) return {{id='finish',label='结束',command={kind='finish'}}} end, decide=function(v) return {allowed=true,reason='',commands={{kind='finish',winners={}}}} end }"),
            ["cpu.lua"]=Encoding.UTF8.GetBytes("return { decide=function(ctx) return ctx.actions[1].command end }")
        };
    }
}
