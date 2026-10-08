using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// A second authored format: explicit validated state and named resource paths.
// It shares no module.json parser and requires no changes to Player or scheduling.
internal sealed class StateDefinitionLoader(RuntimeStartup startup,string source) : IDefinitionLoader
{
    public string Source=>source;
    private JsonObject Read(string directory)=>JsonNode.Parse(File.ReadAllText(DirectoryModule.SafeFile(directory,Source)))!.AsObject();
    public GameDefinition Load(string directory)
    {
        var root=Read(directory);
        if(root["format"]?.GetValue<int>()!=1 || root.Any(p=>p.Key is not ("format" or "state" or "resources"))) throw new InvalidDataException(Source+": unsupported state definition format/field");
        var state=root["state"]!.Deserialize<GameState>(Wire.Options) ?? throw new InvalidDataException(Source+": missing state");
        StateValidation.Validate(state);
        var data=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in root["resources"]!.AsObject())
        {data[file.Key]=File.ReadAllBytes(DirectoryModule.SafeFile(directory,file.Value!.GetValue<string>()));hashes[file.Key]=ModulePackage.Hash(data[file.Key]);}
        foreach(var entity in state.Entities.Values)
            if(entity.FaceResources.Any(p=>!hashes.ContainsKey(p))) throw new InvalidDataException(entity.Id+": unknown face resource");
        var payload=new SortedDictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var capability in new[]{"lua.rules","lua.cpu"})
        {
            var selection=startup.Plugins.FirstOrDefault(p=>p.Id==capability);
            var path=selection?.Configuration.GetProperty("file").GetString() ?? "";
            payload[capability]=JsonSerializer.SerializeToElement(new LuaScript(path.Length==0?"":File.ReadAllText(DirectoryModule.SafeFile(directory,path)),path),Wire.Options);
        }
        var extensions=startup.Plugins.FirstOrDefault(p=>p.Id=="wasm")?.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? [];
        var wasm=new List<RuntimePlugin>();
        foreach(var extension in extensions)
        {
            var bytes=File.ReadAllBytes(DirectoryModule.SafeFile(directory,extension.File!));
            if(bytes.Length>256*1024 || ModulePackage.Hash(bytes)!=extension.Sha256?.ToUpperInvariant()) throw new InvalidDataException(extension.Id+": WASM content hash/size mismatch");
            wasm.Add(new(extension.Id,extension.Version,extension.Api,extension.File!,extension.Sha256!,extension.Permissions ?? [],bytes));
        }
        payload["wasm"]=JsonSerializer.SerializeToElement(wasm,Wire.Options);
        return new(state,hashes,data,payload);
    }
    public IReadOnlyList<string> ContentFiles(string directory)=>[Source,..Read(directory)["resources"]!.AsObject().Select(p=>p.Value!.GetValue<string>())];
    public static RuntimePluginDefinition Plugin(RuntimeStartup startup)=>new("definition.state","1",1,["definition"],[],s=>
        s.Provide("definition",new StateDefinitionLoader(startup,s.Configuration.GetProperty("path").GetString()!)));
}
