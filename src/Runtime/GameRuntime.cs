using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// The fixed bootstrap understands startup and approved capabilities, never game entities or definition filenames.
public static class GameRuntime
{
    public const string Entry="game.start.json";
    public static BoardGame.AppRuntime.GameContent Load(string source,BoardGame.AppRuntime.ContentCatalog catalog,string? preset)=>
        BoardGame.AppRuntime.GameRuntime.Load(source,catalog,preset);
    public static SortedDictionary<string,string> PluginVersions=>new(RuntimeComposition.Catalog().ToDictionary(p=>p.Id,p=>p.Version),StringComparer.Ordinal);
    public static RuntimeStartup ReadStartup(string directory,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        if(!File.Exists(Path.Combine(directory,Entry))) throw new InvalidDataException(Entry+": missing startup entry; explicitly migrate the legacy game package");
        var text=File.ReadAllText(DirectoryModule.SafeFile(directory,Entry));
        if(text.Length>1024*1024) throw new InvalidDataException(Entry+": size limit exceeded");
        using var json=JsonDocument.Parse(text);CheckObject(json.RootElement,["format","id","version","plugins"],Entry);
        foreach(var selection in json.RootElement.GetProperty("plugins").EnumerateArray())
            CheckObject(selection,["id","version","api","configuration"],Entry+".plugins");
        return ValidateStartup(Wire.Read<RuntimeStartup>(text),approvedPlugins);
    }
    internal static IReadOnlyList<RuntimePluginDefinition> Catalog(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)=>
        [..RuntimeComposition.Catalog(startup:startup),..approvedPlugins ?? []];
    internal static RuntimeStartup ValidateStartup(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)=>
        RuntimeDiscovery.Validate(startup,Catalog(startup,approvedPlugins));
    internal static void CheckObject(JsonElement value,IEnumerable<string> fields,string path)
    {
        if(value.ValueKind!=JsonValueKind.Object) throw new InvalidDataException(path+": expected object");
        var names=new HashSet<string>();
        foreach(var property in value.EnumerateObject())
            if(!names.Add(property.Name) || !fields.Contains(property.Name)) throw new InvalidDataException(path+"."+property.Name+": duplicate/unknown configuration");
    }
    public static LoadedModule Load(string source,string? preset=null,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)=>Directory.Exists(source)?LoadDirectory(source,preset,approvedPlugins):
        Path.GetFileName(source).Equals(Entry,StringComparison.OrdinalIgnoreCase)?LoadDirectory(Path.GetDirectoryName(Path.GetFullPath(source))!,preset,approvedPlugins):
        Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase)?GamePackage.Load(source,preset,approvedPlugins):
        preset==null?LoadSnapshot(source,approvedPlugins):throw new InvalidDataException("Snapshots already contain a selected definition preset");
    private static LoadedModule LoadSnapshot(string source,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins)
    {
        var module=ModuleSnapshot.Load(source);
        if(module.RuntimeIdentity==null || module.Definition==null) throw new InvalidDataException("Legacy snapshot: explicitly migrate source and record a new session");
        ValidateSnapshot(module,approvedPlugins);return module;
    }
    public static void ValidateSnapshot(LoadedModule module,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)=>
        BoardGameSnapshot.Validate(module,approvedPlugins);
    public static LoadedModule LoadDirectory(string directory,string? preset=null,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        var startup=GamePresets.Select(ReadStartup(directory,approvedPlugins),preset,approvedPlugins);
        var catalog=Catalog(startup,approvedPlugins);
        var loaderPlan=RuntimeDiscovery.Preparation(startup,catalog);
        using var host=RuntimeHost.StartAsync(loaderPlan,catalog,["definition"]).GetAwaiter().GetResult();
        var loader=host.Service<IDefinitionLoader>("definition");
        var pool=loaderPlan.Plugins.Any(p=>catalog.Single(c=>c.Id==p.Id).Provides.Contains("resources"))?host.Service<ResourceRegistry>("resources").Load(new DirectoryResourceInput(directory)):ResourcePool.Empty;
        var flowFiles=FlowContent.Load(directory,startup);
        var content=ContentFiles(directory,startup,loader,pool,flowFiles,catalog);
        if(content.Count>ModuleContentLimits.MaxFiles-1 || content.Sum(p=>new FileInfo(DirectoryModule.SafeFile(directory,p)).Length)>ModuleContentLimits.MaxTotalBytes)
            throw new InvalidDataException("Directory content size budget exceeded");
        var definition=loader.Load(directory) with {Resources=pool};
        if(flowFiles!=null)
        {
            var flow=FlowContent.Read(startup,flowFiles);
            if(definition.Initial.Ended || definition.Initial.Flow!=null || definition.Initial.Secret!=null) throw new InvalidDataException("flow: definition must contain a fresh non-secret state");
            definition.Initial.Flow=new(){DefinitionHash=flow.Hash,Node=flow.Definition.Nodes.Single(n=>n.Kind==FlowNodeKinds.Start).Id};
        }
        ValidateImages(definition.Initial,pool);
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in content) hashes[file]=ModulePackage.Hash(File.ReadAllBytes(DirectoryModule.SafeFile(directory,file)));
        // Raw startup bytes are not identity: whitespace/key order does not change effective configuration.
        var identity=new RuntimeIdentity(1,startup,loader.Source,hashes);
        var rules=definition.CapabilityData.TryGetValue("lua.rules",out var r)?r.Deserialize<LuaScript>(Wire.Options):null;
        var cpu=definition.CapabilityData.TryGetValue("lua.cpu",out var c)?c.Deserialize<LuaScript>(Wire.Options):null;
        var wasm=definition.CapabilityData.TryGetValue("wasm",out var w)?w.Deserialize<List<RuntimePlugin>>(Wire.Options):null;
        var result=new LoadedModule(startup.Id,startup.Version,"",definition.Initial,rules?.Source ?? "",cpu?.Source ?? "",rules?.Path ?? "",cpu?.Path ?? "",
            definition.ResourceHashes,definition.ResourceData,null,wasm is {Count:>0}?wasm:null,identity,definition,pool,flowFiles);
        return result with {Fingerprint=ModuleSnapshot.Fingerprint(result)};
    }
    internal static IReadOnlyList<string> ContentFiles(string directory,RuntimeStartup startup,IDefinitionLoader loader,ResourcePool? resources=null,SortedDictionary<string,byte[]>? flowFiles=null,IReadOnlyList<RuntimePluginDefinition>? catalog=null)
    {
        var paths=loader.ContentFiles(directory).ToList();
        paths.AddRange(RuntimeDiscovery.Files(startup,catalog ?? Catalog(startup)));
        paths.AddRange(resources?.Files.Keys ?? []);
        paths.AddRange((flowFiles ?? FlowContent.Load(directory,startup))?.Keys.AsEnumerable() ?? []);
        foreach(var path in paths)
        {
            ModulePackage.ValidatePath(path);
            if(path.Equals(Entry,StringComparison.OrdinalIgnoreCase) || path.Equals("package.manifest.json",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(path+": reserved startup/package path");
        }
        var distinct=paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(distinct.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=distinct.Length) throw new InvalidDataException("Duplicate case-insensitive content path");
        return distinct;
    }
    internal static RuntimeHost? StartResources(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins)
    {
        var catalog=Catalog(startup,approvedPlugins);
        var selected=startup.Plugins.Where(p=>catalog.Single(c=>c.Id==p.Id).Content?.Prepare==true && !catalog.Single(c=>c.Id==p.Id).Provides.Contains("definition")).ToList();
        if(selected.Count==0)return null;
        return RuntimeHost.StartAsync(startup with {Plugins=selected},catalog,["resources"]).GetAwaiter().GetResult();
    }
    internal static void ValidateImages(GameState state,ResourcePool resources)
    {
        DocumentFile.ValidateReferences(resources.Documents,state,resources);
        if(resources.Animations is {} animations)AnimationFile.ValidateReferences(animations,state,resources);
        foreach(var entity in state.Entities.Values)
            foreach(var name in entity.FaceResources)
                try {var image=resources.GetImage(name);_=resources.GetFile(image.Resource);}
                catch(Exception e) when(e is InvalidDataException) {throw new InvalidDataException(entity.Id+".faces: "+e.Message,e);}
    }
}
