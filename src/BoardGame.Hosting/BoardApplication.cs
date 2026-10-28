using System.Runtime.CompilerServices;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Resources;

namespace BoardGame.Runtime;

// An explicit ordinary board assembly. Script/effect implementations are supplied separately.
public sealed class BoardApplication(uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500)
{
    private readonly ConditionalWeakTable<GameContent,Binding> bindings=new();
    private sealed record Binding(LoadedModule Module,BoardContentPolicy Policy);
    private sealed record Payload(GameState State,AssetSet Assets);
    public LoadedModule Module(GameContent content)=>Wire.Copy(Bound(content).Module);
    public GameDefinition Definition(GameContent content)=>Wire.Copy(Bound(content).Module.Definition!);
    public void Validate(GameContent content,LoadedModule module)=>Bound(content).Policy.Validate(module);
    private Binding Bound(GameContent content)=>bindings.GetValue(content,CreateBinding);
    public IEnumerable<ApprovedApplicationPlugin> Plugins()
    {
        yield return new(content=>DefinitionPlugin());
        foreach(var descriptor in AssetPlugins.Catalog())
        {
            var id=descriptor.Id;
            yield return new(content=>AssetPlugins.Catalog(()=>content==null?null:Bound(content).Module.Resources).Single(p=>p.Id==id));
        }
        yield return new(content=>AuthorityPlugin.Create(()=>Bound(Required(content)).Module,seed));
        yield return new(_=>CommandRulesPlugin.Create());
        yield return new(content=>RecordingPlugin.Create(()=>Bound(Required(content)).Module,value=>Bound(Required(content)).Policy.Validate(value)));
        yield return new(content=>
        {
            var module=content==null?null:Bound(content).Module;
            if(module!=null && controllers?.Any(p=>!module.Initial.Seats.Any(s=>s.Id==p.Key) || p.Value is not ("human" or "cpu"))==true)
                throw new ArgumentException("Invalid seat/controller configuration");
            var selected=module?.RuntimeIdentity!.Startup.Plugins.Select(p=>p.Id).ToHashSet() ?? [];
            var control=module?.Initial.Seats.ToDictionary(s=>s.Id,s=>controllers?.GetValueOrDefault(s.Id) ?? s.Controller);
            return BoardSessionPlugin.Create(()=>Bound(Required(content)).Module,seed,control,maxSteps,
                new(value=>Bound(Required(content)).Policy.Validate(value),_=>[]),selected.Contains("lua.cpu") || control?.Values.Contains("cpu")==true,selected.Contains("wasm"));
        });
    }
    private static GameContent Required(GameContent? content)=>content ?? throw new InvalidOperationException("Runtime content is required");
    private static Binding CreateBinding(GameContent content)
    {
        if(content.Payload.Format!="board.state" || content.Payload.Version!=1)throw new InvalidDataException("Unsupported board payload");
        var payload=content.Payload.Data.Deserialize<Payload>(Wire.Options) ?? throw new InvalidDataException("Missing board payload");
        StateValidation.Validate(payload.State);var identity=content.Identity;var startup=identity.Startup;
        LuaScript Script(string id)
        {
            var selection=startup.Plugins.SingleOrDefault(p=>p.Id==id);var path=selection?.Configuration.GetProperty("file").GetString() ?? "";
            var text=path.Length==0?"":System.Text.Encoding.UTF8.GetString(content.File(path).Span);
            return new(text.StartsWith('\uFEFF')?text[1..]:text,path);
        }
        var rules=Script("lua.rules");var cpu=Script("lua.cpu");
        var extensions=startup.Plugins.SingleOrDefault(p=>p.Id=="wasm")?.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? [];
        var wasm=extensions.Select(e=>new RuntimePlugin(e.Id,e.Version,e.Api,e.File!,e.Sha256!,e.Permissions ?? [],content.File(e.File!).ToArray())).ToList();
        var assets=payload.Assets;var resources=new ResourcePool(assets.Files,assets.Images,assets.Binaries,assets.Font);
        var module=new LoadedModule(startup.Id,startup.Version,"",payload.State,rules.Source,cpu.Source,rules.Path,cpu.Path,
            RuntimePlugins:wasm.Count>0?wasm:null,RuntimeIdentity:new(1,startup,identity.DefinitionSource,identity.Files),Resources:resources);
        module=module with {Definition=RuntimeDefinition.From(module)};
        module=module with {Fingerprint=ModuleSnapshot.Fingerprint(module)};
        var frozen=Wire.Json(module);
        // This bounded content instance cannot substitute a different source/version through a record.
        return new(module,new(candidate=>
        {
            if(candidate.Fingerprint!=ModuleSnapshot.Fingerprint(candidate) || Wire.Json(candidate)!=frozen)
                throw new InvalidDataException("Board recording content differs from the selected application snapshot");
        },_=>[]));
    }
    private static RuntimePluginDefinition DefinitionPlugin()=>new("definition.board-state","1",1,["definition"],["resources"],scope=>
        scope.Provide("definition",new Loader(scope.Configuration.GetProperty("path").GetString()!,scope.Service<AssetRegistry>("resources"))))
        {Content=new(["path"],RequiredCapabilities:[..AuthorityPlugin.Required,"rules","session","recording"],Prepare:true,
            Definition:config=>new(config.GetProperty("path").GetString()!,new Dictionary<string,string>()))};
    private sealed class Loader(string path,AssetRegistry assets) : IContentLoader
    {
        public string PayloadFormat=>"board.state";
        public int PayloadVersion=>1;
        public JsonElement Load(IContentSource source)
        {
            var state=BoardStateDocument.Parse(System.Text.Encoding.UTF8.GetString(source.Read(path).Span),"definition.board-state: "+path);
            return JsonSerializer.SerializeToElement(new Payload(state,assets.Load(new ContentResourceInput(source))),Wire.Options);
        }
        public JsonElement Load(IContentSource source,RuntimeStartup startup)
        {
            var extensions=startup.Plugins.SingleOrDefault(p=>p.Id=="wasm")?.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? [];
            foreach(var extension in extensions)
            {
                var bytes=source.Read(extension.File!);
                if(bytes.Length>256*1024 || ModulePackage.Hash(bytes.ToArray())!=extension.Sha256?.ToUpperInvariant())
                    throw new InvalidDataException(extension.Id+": WASM content hash/size mismatch");
            }
            return Load(source);
        }
    }
}
