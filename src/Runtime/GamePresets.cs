using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// Definition presets select authored files, not host-side rules or entity conventions.
public static class GamePresets
{
    public static IReadOnlyDictionary<string,string> Choices(LoadedModule module)=>Choices(module.RuntimeIdentity!.Startup);
    internal static IReadOnlyDictionary<string,string> Choices(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)=>
        RuntimeDiscovery.Definition(startup,GameRuntime.Catalog(startup,approvedPlugins)).Sources.Presets;
    public static string? Selected(LoadedModule module)
    {
        var source=module.RuntimeIdentity!.DefinitionSource;
        return Choices(module).FirstOrDefault(p=>p.Value==source).Key;
    }
    internal static RuntimeStartup Select(RuntimeStartup startup,string? preset,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        var catalog=GameRuntime.Catalog(startup,approvedPlugins);
        var (selection,sources)=RuntimeDiscovery.Definition(startup,catalog);
        var choices=sources.Presets;
        if(preset==null) return startup;
        if(!choices.TryGetValue(preset,out var source)) throw new InvalidDataException("Unknown definition preset: "+preset);
        return startup with {Plugins=startup.Plugins.Select(p=>
        {
            if(p.Id!=selection.Id) return p;
            var replace=catalog.Single(c=>c.Id==p.Id).Content?.SelectSource ?? throw new InvalidDataException(p.Id+": preset selection unavailable");
            return p with {Configuration=replace(p.Configuration,source)};
        }).ToList()};
    }
}
