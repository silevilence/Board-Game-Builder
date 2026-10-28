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
    public GameDefinition Load(string directory)
    {
        var state=BoardStateDocument.Parse(File.ReadAllText(DirectoryModule.SafeFile(directory,Source)),"definition.state: "+Source);
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
