using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// The fixed bootstrap understands startup and approved capabilities, never game entities or definition filenames.
public static class GameRuntime
{
    public const string Entry="game.start.json";
    public static SortedDictionary<string,string> PluginVersions=>new(RuntimeComposition.Catalog().ToDictionary(p=>p.Id,p=>p.Version),StringComparer.Ordinal);
    public static RuntimeStartup ReadStartup(string directory)
    {
        if(!File.Exists(Path.Combine(directory,Entry))) throw new InvalidDataException(Entry+": missing startup entry; explicitly migrate the legacy game package");
        var text=File.ReadAllText(DirectoryModule.SafeFile(directory,Entry));
        if(text.Length>1024*1024) throw new InvalidDataException(Entry+": size limit exceeded");
        using var json=JsonDocument.Parse(text);CheckObject(json.RootElement,["format","id","version","plugins"],Entry);
        foreach(var selection in json.RootElement.GetProperty("plugins").EnumerateArray())
            CheckObject(selection,["id","version","api","configuration"],Entry+".plugins");
        return ValidateStartup(Wire.Read<RuntimeStartup>(text));
    }
    private static RuntimeStartup ValidateStartup(RuntimeStartup startup)
    {
        var catalog=RuntimeComposition.Catalog(startup:startup);
        RuntimeHost.Validate(startup,catalog,AuthorityPlugin.Required.Concat(["definition","rules","session","recording"]));
        foreach(var selection in startup.Plugins)
        {
            string[] fields=selection.Id switch
            {
                "definition.module"=>["path","presets"],"definition.state"=>["path"],"lua.rules" or "lua.cpu"=>["file"],"wasm"=>["extensions"],
                "rules.commands"=>["actions"],"flow"=>["path","maxAutomaticSteps"],_=>[]
            };
            if(ResourcePlugins.IsResource(selection.Id)) fields=selection.Id=="resources.font"?["path","license"]:["sources"];
            CheckObject(selection.Configuration,fields,selection.Id+".configuration");
            if(selection.Id=="resources.font") _=FontResourceLoader.Read(selection.Configuration);
            else if(ResourcePlugins.IsResource(selection.Id)) _=ResourceSource.Read(selection.Configuration,selection.Id);
            if(selection.Id=="rules.commands") _=new CommandRuleProvider(selection.Configuration);
            if(selection.Id=="flow")
            {
                ModulePackage.ValidatePath(selection.Configuration.GetProperty("path").GetString()!);
                if(selection.Configuration.TryGetProperty("maxAutomaticSteps",out var limit) && (!limit.TryGetInt32(out var steps) || steps is <1 or >100000)) throw new InvalidDataException("flow: maxAutomaticSteps must be 1..100000");
                if(startup.Plugins.Any(p=>p.Id=="wasm")) throw new InvalidDataException("flow: WASM effects are not supported by this version");
            }
            if(selection.Id is "definition.module" or "definition.state") ModulePackage.ValidatePath(selection.Configuration.GetProperty("path").GetString()!);
            if(selection.Id is "lua.rules" or "lua.cpu") ModulePackage.ValidatePath(selection.Configuration.GetProperty("file").GetString()!);
            if(selection.Id=="wasm")
            {
                var extensions=selection.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? throw new InvalidDataException("wasm: missing extensions");
                if(extensions.Any(p=>p.Kind!="wasm" || p.EditorOnly)) throw new InvalidDataException("wasm: expected runtime WASM extension");
                ModulePackage.ValidateDependencies(extensions);
            }
        }
        _=GamePresets.Choices(startup);
        return startup with {Plugins=startup.Plugins.OrderBy(p=>p.Id,StringComparer.Ordinal).ToList()};
    }
    internal static void CheckObject(JsonElement value,IEnumerable<string> fields,string path)
    {
        if(value.ValueKind!=JsonValueKind.Object) throw new InvalidDataException(path+": expected object");
        var names=new HashSet<string>();
        foreach(var property in value.EnumerateObject())
            if(!names.Add(property.Name) || !fields.Contains(property.Name)) throw new InvalidDataException(path+"."+property.Name+": duplicate/unknown configuration");
    }
    public static LoadedModule Load(string source,string? preset=null)=>Directory.Exists(source)?LoadDirectory(source,preset):
        Path.GetFileName(source).Equals(Entry,StringComparison.OrdinalIgnoreCase)?LoadDirectory(Path.GetDirectoryName(Path.GetFullPath(source))!,preset):
        Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase)?GamePackage.Load(source,preset):
        preset==null?LoadSnapshot(source):throw new InvalidDataException("Snapshots already contain a selected definition preset");
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
        ValidateStartup(identity.Startup);
        var flowSelection=FlowContent.Selection(identity.Startup);
        if(flowSelection==null)
        {
            if(module.FlowFiles!=null || module.Initial.Flow!=null) throw new InvalidDataException("flow: payload without enabled plugin");
        }
        else
        {
            var flow=FlowContent.Read(identity.Startup,module.FlowFiles ?? throw new InvalidDataException("flow: missing payload"));
            foreach(var file in module.FlowFiles!)
                if(identity.ContentHashes.GetValueOrDefault(file.Key)!=ModulePackage.Hash(file.Value)) throw new InvalidDataException("flow: content identity mismatch");
            var expected=new FlowState {DefinitionHash=flow.Hash,Node=flow.Definition.Nodes.Single(n=>n.Kind==FlowNodeKinds.Start).Id};
            if(Wire.Json(expected)!=Wire.Json(module.Initial.Flow)) throw new InvalidDataException("flow: initial cursor mismatch");
        }
        StateValidation.Validate(module.Initial);
        var resourceHashes=module.ResourceHashes ?? new(StringComparer.Ordinal);
        var resourceData=module.ResourceData ?? new(StringComparer.Ordinal);
        if(resourceData.Count>0)throw new InvalidDataException("Legacy resource snapshot: explicitly migrate resource declarations and regenerate the snapshot");
        if(!resourceHashes.Keys.SequenceEqual(resourceData.Keys) || resourceHashes.Any(p=>ModulePackage.Hash(resourceData[p.Key])!=p.Value))
            throw new InvalidDataException("Resource snapshot content hash mismatch");
        var pool=module.Resources ?? ResourcePool.Empty;
        foreach(var file in pool.Files)
            if(identity.ContentHashes.GetValueOrDefault(file.Key)!=ModulePackage.Hash(file.Value.ToArray()))throw new InvalidDataException(file.Key+": resource content identity mismatch");
        using(var resourceHost=StartResources(identity.Startup))
        {
            var rebuilt=resourceHost==null?ResourcePool.Empty:resourceHost.Service<ResourceRegistry>("resources").Load(new SnapshotResourceInput(pool));
            if(Wire.Json(rebuilt)!=Wire.Json(pool))throw new InvalidDataException("Resource pool snapshot/configuration mismatch");
        }
        ValidateImages(module.Initial,pool);
        foreach(var path in identity.ContentHashes.Keys) ModulePackage.ValidatePath(path);
        var loader=identity.Startup.Plugins.Single(p=>p.Id is "definition.module" or "definition.state");
        if(identity.DefinitionSource!=loader.Configuration.GetProperty("path").GetString() || !identity.ContentHashes.ContainsKey(identity.DefinitionSource))
            throw new InvalidDataException("Definition source identity mismatch");
        var expanded=RuntimeDefinition.From(module with {Definition=null});
        if(Wire.Json(expanded)!=Wire.Json(module.Definition)) throw new InvalidDataException("Definition/capability snapshot conflict");
        foreach(var capability in new[]{"lua.rules","lua.cpu"})
        {
            var selection=identity.Startup.Plugins.SingleOrDefault(p=>p.Id==capability);
            var script=capability=="lua.rules"?module.Rules:module.Cpu;
            var path=capability=="lua.rules"?module.RulesPath:module.CpuPath;
            if(selection==null ? script.Length!=0 || path.Length!=0 : path!=selection.Configuration.GetProperty("file").GetString())
                throw new InvalidDataException(capability+": script configuration identity mismatch");
        }
        var extensions=RuntimeContent.Extensions(identity.Startup);
        var payload=module.RuntimePlugins ?? [];
        if(extensions.Count!=payload.Count) throw new InvalidDataException("WASM configuration identity mismatch");
        foreach(var extension in extensions)
        {
            var plugin=payload.SingleOrDefault(p=>p.Id==extension.Id);
            if(plugin==null || plugin.Version!=extension.Version || plugin.Api!=extension.Api || plugin.File!=extension.File ||
                !string.Equals(plugin.Sha256,extension.Sha256,StringComparison.OrdinalIgnoreCase) ||
                !plugin.Permissions.SequenceEqual(extension.Permissions ?? []) ||
                ModulePackage.Hash(plugin.Code)!=extension.Sha256?.ToUpperInvariant() ||
                identity.ContentHashes.GetValueOrDefault(plugin.File)!=ModulePackage.Hash(plugin.Code))
                throw new InvalidDataException(extension.Id+": WASM configuration/content identity mismatch");
        }
        foreach(var selection in RuntimeContent.Scripts(identity.Startup))
        {
            var path=selection.Configuration.GetProperty("file").GetString()!;
            var script=selection.Id=="lua.rules"?module.Rules:module.Cpu;
            var data=System.Text.Encoding.UTF8.GetBytes(script);var hash=identity.ContentHashes.GetValueOrDefault(path);
            if(hash!=ModulePackage.Hash(data) && hash!=ModulePackage.Hash([0xEF,0xBB,0xBF,..data])) throw new InvalidDataException(path+": script content hash mismatch (UTF-8 required)");
        }
    }
    public static LoadedModule LoadDirectory(string directory,string? preset=null)
    {
        var startup=GamePresets.Select(ReadStartup(directory),preset);
        var catalog=RuntimeComposition.Catalog(startup:startup);
        RuntimeHost.Validate(startup,catalog,AuthorityPlugin.Required.Concat(["definition","rules","session","recording"]));
        var loaders=catalog.Where(p=>(p.Provides.Contains("definition") || ResourcePlugins.IsResource(p.Id)) && startup.Plugins.Any(s=>s.Id==p.Id)).ToArray();
        var loaderPlan=startup with {Plugins=startup.Plugins.Where(p=>loaders.Any(l=>l.Id==p.Id)).ToList()};
        using var host=RuntimeHost.StartAsync(loaderPlan,loaders,["definition"]).GetAwaiter().GetResult();
        var loader=host.Service<IDefinitionLoader>("definition");
        var pool=startup.Plugins.Any(p=>p.Id=="resources")?host.Service<ResourceRegistry>("resources").Load(new DirectoryResourceInput(directory)):ResourcePool.Empty;
        var flowFiles=FlowContent.Load(directory,startup);
        var content=ContentFiles(directory,startup,loader,pool,flowFiles);
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
    internal static IReadOnlyList<string> ContentFiles(string directory,RuntimeStartup startup,IDefinitionLoader loader,ResourcePool? resources=null,SortedDictionary<string,byte[]>? flowFiles=null)
    {
        var paths=loader.ContentFiles(directory).ToList();
        paths.AddRange(RuntimeContent.Files(startup));
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
    private static RuntimeHost? StartResources(RuntimeStartup startup)
    {
        var selected=startup.Plugins.Where(p=>ResourcePlugins.IsResource(p.Id)).ToList();
        if(selected.Count==0)return null;
        return RuntimeHost.StartAsync(startup with {Plugins=selected},ResourcePlugins.Catalog(()=>null).ToArray(),["resources"]).GetAwaiter().GetResult();
    }
    private static void ValidateImages(GameState state,ResourcePool resources)
    {
        foreach(var entity in state.Entities.Values)
            foreach(var name in entity.FaceResources)
                try {var image=resources.GetImage(name);_=resources.GetFile(image.Resource);}
                catch(Exception e) when(e is InvalidDataException) {throw new InvalidDataException(entity.Id+".faces: "+e.Message,e);}
    }
}
