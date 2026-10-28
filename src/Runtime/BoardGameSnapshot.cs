using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// Compatibility adapter for the existing board-game snapshot payload.
internal static class BoardGameSnapshot
{
    public static void Validate(LoadedModule module,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        var identity=module.RuntimeIdentity;
        if(identity==null || identity.Format!=1 || module.Definition==null || identity.Startup.Id!=module.Id || identity.Startup.Version!=module.Version)
            throw new InvalidDataException("Invalid/legacy runtime identity");
        if(module.Fingerprint!=ModuleSnapshot.Fingerprint(module)) throw new InvalidDataException("Runtime identity fingerprint mismatch");
        GameRuntime.ValidateStartup(identity.Startup,approvedPlugins);
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
        using(var resourceHost=GameRuntime.StartResources(identity.Startup,approvedPlugins))
        {
            var rebuilt=resourceHost==null?ResourcePool.Empty:resourceHost.Service<ResourceRegistry>("resources").Load(new SnapshotResourceInput(pool));
            if(Wire.Json(rebuilt)!=Wire.Json(pool))throw new InvalidDataException("Resource pool snapshot/configuration mismatch");
        }
        GameRuntime.ValidateImages(module.Initial,pool);
        foreach(var path in identity.ContentHashes.Keys) ModulePackage.ValidatePath(path);
        var source=RuntimeDiscovery.Definition(identity.Startup,GameRuntime.Catalog(identity.Startup,approvedPlugins)).Sources.Source;
        if(identity.DefinitionSource!=source || !identity.ContentHashes.ContainsKey(identity.DefinitionSource))
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
}
