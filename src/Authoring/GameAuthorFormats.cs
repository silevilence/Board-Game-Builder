using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public interface IGameAuthorFormat
{
    string DefinitionPath {get;}
    // Structured data alone does not imply the module author model (entities/prototypes/layouts).
    IReadOnlySet<string> Capabilities => new HashSet<string>();
    JsonObject Read(IAuthorFiles document);
    string Serialize(IAuthorFiles document,JsonObject configuration);
}

public sealed record GameAuthorFormatProvider(string LoaderId,string Version,int Api,Func<RuntimePluginSelection,IGameAuthorFormat> Create);

// Per-workspace author support. Runtime loader availability does not imply a matching editor.
public sealed class GameAuthorFormats
{
    public const string BoardModel = "board.author-model";
    public long Revision { get; private set; }
    private readonly Dictionary<(string Id,string Version,int Api),GameAuthorFormatProvider> providers=new();
    public event Action? Changed;
    public GameAuthorFormats(bool includeDefault=true)
    {
        if(includeDefault) Register(ModuleProvider);
    }
    public static GameAuthorFormatProvider ModuleProvider=>new("definition.module","1",1,selection=>new ModuleJson(selection.Configuration.GetProperty("path").GetString()!));
    public IDisposable Register(GameAuthorFormatProvider provider)
    {
        var key=(provider.LoaderId,provider.Version,provider.Api);
        if(!providers.TryAdd(key,provider)) throw new InvalidOperationException("Duplicate author format provider: "+provider.LoaderId);
        Revision++;Changed?.Invoke();
        return new Registration(()=> {if(providers.Remove(key)) {Revision++;Changed?.Invoke();}});
    }
    private sealed record ModuleJson(string DefinitionPath) : IGameAuthorFormat
    {
        public IReadOnlySet<string> Capabilities => new HashSet<string> { BoardModel };
        public JsonObject Read(IAuthorFiles document)=>JsonNode.Parse(document.Text(DefinitionPath))!.AsObject();
        public string Serialize(IAuthorFiles document,JsonObject configuration)=>configuration.ToJsonString(new(){WriteIndented=true});
    }
    public static GameAuthorFormatProvider StateProvider => new("definition.state","1",1,
        selection=>new StateJson(selection.Configuration.GetProperty("path").GetString()!));
    private sealed record StateJson(string DefinitionPath) : IGameAuthorFormat
    {
        public JsonObject Read(IAuthorFiles document)=>JsonNode.Parse(document.Text(DefinitionPath))!["state"]!.DeepClone().AsObject();
        public string Serialize(IAuthorFiles document,JsonObject configuration)
        {
            var root=JsonNode.Parse(document.Text(DefinitionPath))!.AsObject();
            root["state"]=configuration.DeepClone();
            return root.ToJsonString(new(){WriteIndented=true});
        }
    }
    public IGameAuthorFormat? Resolve(IAuthorFiles document)
    {
        var startup=JsonNode.Parse(document.Text(GameRuntime.Entry))!.Deserialize<RuntimeStartup>(Wire.Options)!;
        var matches=startup.Plugins.Where(p=>providers.ContainsKey((p.Id,p.Version,p.Api))).ToArray();
        if(matches.Length>1) throw new InvalidOperationException("Multiple author format providers match this document");
        return matches.Length==0?null:providers[(matches[0].Id,matches[0].Version,matches[0].Api)].Create(matches[0]);
    }
    private sealed class Registration(Action release) : IDisposable { private Action? cleanup=release; public void Dispose()=>Interlocked.Exchange(ref cleanup,null)?.Invoke(); }
    public static Dictionary<string,byte[]> Template()
    {
        RuntimePluginSelection Select(string id,object config)=>new(id,"1",1,JsonSerializer.SerializeToElement(config,Wire.Options));
        var startup=new RuntimeStartup(1,"untitled","1.0.0",
            [Select("definition.module",new {path="module.json"}),Select("kernel",new {}),Select("lua.rules",new {file="rules.lua"}),
                Select("lua.cpu",new {file="cpu.lua"}),Select("session.local",new {}),Select("recording",new {}),Select("resources",new {}),Select("resources.images",new {}),BoardRenderContract.Selection]);
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
