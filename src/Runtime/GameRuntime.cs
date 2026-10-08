using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// The fixed bootstrap understands startup and approved capabilities, never game entities or definition filenames.
public static class GameRuntime
{
    public const string Entry="game.start.json";
    public static RuntimeStartup ReadStartup(string directory)
    {
        if(!File.Exists(Path.Combine(directory,Entry))) throw new InvalidDataException(Entry+": missing startup entry; explicitly migrate the legacy game package");
        var text=File.ReadAllText(DirectoryModule.SafeFile(directory,Entry));
        if(text.Length>1024*1024) throw new InvalidDataException(Entry+": size limit exceeded");
        using var json=JsonDocument.Parse(text);CheckObject(json.RootElement,["format","id","version","plugins"],Entry);
        foreach(var selection in json.RootElement.GetProperty("plugins").EnumerateArray())
            CheckObject(selection,["id","version","api","configuration"],Entry+".plugins");
        var startup=Wire.Read<RuntimeStartup>(text);
        foreach(var selection in startup.Plugins)
        {
            string[] fields=selection.Id switch
            {
                "definition.module"=>["path"],"lua.rules" or "lua.cpu"=>["file"],"wasm"=>["extensions"],
                "rules.commands"=>["actions"],_=>[]
            };
            CheckObject(selection.Configuration,fields,selection.Id+".configuration");
            if(selection.Id=="definition.module") ModulePackage.ValidatePath(selection.Configuration.GetProperty("path").GetString()!);
            if(selection.Id is "lua.rules" or "lua.cpu") ModulePackage.ValidatePath(selection.Configuration.GetProperty("file").GetString()!);
            if(selection.Id=="wasm")
            {
                var extensions=selection.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? throw new InvalidDataException("wasm: missing extensions");
                if(extensions.Any(p=>p.Kind!="wasm" || p.EditorOnly)) throw new InvalidDataException("wasm: expected runtime WASM extension");
                ModulePackage.ValidateDependencies(extensions);
            }
        }
        return startup with {Plugins=startup.Plugins.OrderBy(p=>p.Id,StringComparer.Ordinal).ToList()};
    }
    internal static void CheckObject(JsonElement value,IEnumerable<string> fields,string path)
    {
        if(value.ValueKind!=JsonValueKind.Object) throw new InvalidDataException(path+": expected object");
        var names=new HashSet<string>();
        foreach(var property in value.EnumerateObject())
            if(!names.Add(property.Name) || !fields.Contains(property.Name)) throw new InvalidDataException(path+"."+property.Name+": duplicate/unknown configuration");
    }
    public static LoadedModule Load(string source)=>Directory.Exists(source)?LoadDirectory(source):
        Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase)?GamePackage.Load(source):LoadSnapshot(source);
    private static LoadedModule LoadSnapshot(string source)
    {
        var module=ModuleSnapshot.Load(source);
        if(module.RuntimeIdentity==null || module.Definition==null) throw new InvalidDataException("Legacy snapshot: explicitly migrate source and record a new session");
        ValidateSnapshot(module);return module;
    }
    public static void ValidateSnapshot(LoadedModule module)
    {
        var identity=module.RuntimeIdentity;
        if(identity==null || identity.Format!=1 || module.Definition==null || identity.Startup.Id!=module.Id || identity.Startup.Version!=module.Version)
            throw new InvalidDataException("Invalid/legacy runtime identity");
        if(module.Fingerprint!=ModuleSnapshot.Fingerprint(module)) throw new InvalidDataException("Runtime identity fingerprint mismatch");
        var expanded=RuntimeDefinition.From(module with {Definition=null});
        if(Wire.Json(expanded)!=Wire.Json(module.Definition)) throw new InvalidDataException("Definition/capability snapshot conflict");
        foreach(var selection in identity.Startup.Plugins.Where(p=>p.Id is "lua.rules" or "lua.cpu"))
        {
            var path=selection.Configuration.GetProperty("file").GetString()!;
            var script=selection.Id=="lua.rules"?module.Rules:module.Cpu;
            var data=System.Text.Encoding.UTF8.GetBytes(script);var hash=identity.ContentHashes.GetValueOrDefault(path);
            if(hash!=ModulePackage.Hash(data) && hash!=ModulePackage.Hash([0xEF,0xBB,0xBF,..data])) throw new InvalidDataException(path+": script content hash mismatch (UTF-8 required)");
        }
    }
    public static LoadedModule LoadDirectory(string directory)
    {
        var startup=ReadStartup(directory);
        var placeholder=new LoadedModule(startup.Id,startup.Version,"",new());
        var catalog=RuntimeComposition.Catalog(placeholder,1,new Dictionary<string,string>(),500,startup);
        RuntimeHost.Validate(startup,catalog,AuthorityPlugin.Required.Concat(["definition","rules","session","recording"]));
        var loaders=catalog.Where(p=>p.Provides.Contains("definition") && startup.Plugins.Any(s=>s.Id==p.Id)).ToArray();
        var loaderPlan=startup with {Plugins=startup.Plugins.Where(p=>loaders.Any(l=>l.Id==p.Id)).ToList()};
        using var host=RuntimeHost.StartAsync(loaderPlan,loaders,["definition"]).GetAwaiter().GetResult();
        var loader=host.Service<IDefinitionLoader>("definition");
        var definition=loader.Load(directory);
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in ContentFiles(directory,startup,loader)) hashes[file]=ModulePackage.Hash(File.ReadAllBytes(DirectoryModule.SafeFile(directory,file)));
        // Raw startup bytes are not identity: whitespace/key order does not change effective configuration.
        var identity=new RuntimeIdentity(1,startup,loader.Source,hashes);
        var rules=definition.CapabilityData.TryGetValue("lua.rules",out var r)?r.Deserialize<LuaScript>(Wire.Options):null;
        var cpu=definition.CapabilityData.TryGetValue("lua.cpu",out var c)?c.Deserialize<LuaScript>(Wire.Options):null;
        var wasm=definition.CapabilityData.TryGetValue("wasm",out var w)?w.Deserialize<List<RuntimePlugin>>(Wire.Options):null;
        var result=new LoadedModule(startup.Id,startup.Version,"",definition.Initial,rules?.Source ?? "",cpu?.Source ?? "",rules?.Path ?? "",cpu?.Path ?? "",
            definition.ResourceHashes,definition.ResourceData,null,wasm is {Count:>0}?wasm:null,identity,definition);
        return result with {Fingerprint=ModuleSnapshot.Fingerprint(result)};
    }
    internal static IReadOnlyList<string> ContentFiles(string directory,RuntimeStartup startup,IDefinitionLoader loader)
    {
        var paths=loader.ContentFiles(directory).ToList();
        foreach(var plugin in startup.Plugins)
        {
            if(plugin.Id is "lua.rules" or "lua.cpu") paths.Add(plugin.Configuration.GetProperty("file").GetString()!);
            if(plugin.Id=="wasm") paths.AddRange(plugin.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options)!.Select(p=>p.File!));
        }
        foreach(var path in paths)
        {
            ModulePackage.ValidatePath(path);
            if(path.Equals(Entry,StringComparison.OrdinalIgnoreCase) || path.Equals("package.manifest.json",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(path+": reserved startup/package path");
        }
        return paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
