using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// A second authored format: explicit validated state, independent of resource formats.
// It shares no module.json parser and requires no changes to Player or scheduling.
internal sealed class StateDefinitionLoader(RuntimeStartup startup,string source) : IDefinitionLoader
{
    public string Source=>source;
    private JsonObject Read(string directory)
    {
        var location="definition.state: "+Source;
        try
        {
            var root=JsonNode.Parse(File.ReadAllText(DirectoryModule.SafeFile(directory,Source))) as JsonObject
                ?? throw new InvalidDataException("$: expected object");
            if(root["format"] is not JsonValue format || !format.TryGetValue<int>(out var version) || version!=1)
                throw new InvalidDataException("$.format: expected supported version 1");
            if(root.Any(p=>p.Key is not ("format" or "state" or "resources"))) throw new InvalidDataException("$: unknown state definition field");
            if(root["state"] is not JsonObject) throw new InvalidDataException("$.state: required object");
            if(root.ContainsKey("resources"))
            {
                if(root["resources"] is not JsonObject resources)throw new InvalidDataException("$.resources: expected object");
                foreach(var resource in resources)
                    if(resource.Value is not JsonValue value || !value.TryGetValue<string>(out _))throw new InvalidDataException("$.resources."+resource.Key+": expected path");
                if(resources.Count>0)throw new InvalidDataException("$.resources: migrate definition resources to game.start.json resource plugins");
            }
            return root;
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {throw new InvalidDataException(location+": "+error.Message,error);}
    }
    public GameDefinition Load(string directory)
    {
        var root=Read(directory);
        GameState state;
        try
        {
            state=root["state"]!.Deserialize<GameState>(Wire.Options) ?? throw new InvalidDataException("required object");
            StateValidation.Validate(state);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {throw new InvalidDataException("definition.state: "+Source+" $.state: "+error.Message,error);}
        var data=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        var payload=new SortedDictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var capability in new[]{"lua.rules","lua.cpu"})
        {
            var selection=startup.Plugins.FirstOrDefault(p=>p.Id==capability);
            var path=selection?.Configuration.GetProperty("file").GetString() ?? "";
            payload[capability]=JsonSerializer.SerializeToElement(new LuaScript(path.Length==0?"":File.ReadAllText(DirectoryModule.SafeFile(directory,path)),path),Wire.Options);
        }
        var extensions=RuntimeContent.Extensions(startup);
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
    public IReadOnlyList<string> ContentFiles(string directory)=>[Source];
    public static RuntimePluginDefinition Plugin(Func<RuntimeStartup> startup)=>new("definition.state","1",1,["definition"],[],s=>
        s.Provide("definition",new StateDefinitionLoader(startup(),s.Configuration.GetProperty("path").GetString()!)))
        {Content=DefinitionContent.Contract(false),ServicePermissions=new Dictionary<string,string>{{"definition",AuthorityPlugin.Access}}};
}
