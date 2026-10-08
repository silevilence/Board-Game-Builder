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
    string Serialize(ModuleDocument document,JsonObject configuration);
}

public sealed record GameAuthorFormatProvider(string LoaderId,string Version,int Api,Func<RuntimePluginSelection,IGameAuthorFormat> Create);

// Per-workspace author support. Runtime loader availability does not imply a matching editor.
public sealed class GameAuthorFormats
{
    private readonly Dictionary<(string Id,string Version,int Api),GameAuthorFormatProvider> providers=new();
    public event Action? Changed;
    public GameAuthorFormats()
    {
        Register(new("definition.module","1",1,selection=>new ModuleJson(selection.Configuration.GetProperty("path").GetString()!)));
    }
    public IDisposable Register(GameAuthorFormatProvider provider)
    {
        var key=(provider.LoaderId,provider.Version,provider.Api);
        if(!providers.TryAdd(key,provider)) throw new InvalidOperationException("Duplicate author format provider: "+provider.LoaderId);
        Changed?.Invoke();
        return new Cleanup(()=> {if(providers.Remove(key)) Changed?.Invoke();});
    }
    private sealed record ModuleJson(string DefinitionPath) : IGameAuthorFormat
    {
        public JsonObject Read(ModuleDocument document)=>JsonNode.Parse(document.Text(DefinitionPath))!.AsObject();
        public string Serialize(ModuleDocument document,JsonObject configuration)=>configuration.ToJsonString(new(){WriteIndented=true});
    }
    public IGameAuthorFormat? Resolve(ModuleDocument document)
    {
        var startup=document.Startup.Deserialize<RuntimeStartup>(Wire.Options)!;
        var matches=startup.Plugins.Where(p=>providers.ContainsKey((p.Id,p.Version,p.Api))).ToArray();
        if(matches.Length>1) throw new InvalidOperationException("Multiple author format providers match this document");
        return matches.Length==0?null:providers[(matches[0].Id,matches[0].Version,matches[0].Api)].Create(matches[0]);
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
