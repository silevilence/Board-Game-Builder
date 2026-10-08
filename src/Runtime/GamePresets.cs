using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// Definition presets select authored files, not host-side rules or entity conventions.
public static class GamePresets
{
    public static IReadOnlyDictionary<string,string> Choices(LoadedModule module)=>Choices(module.RuntimeIdentity!.Startup);
    internal static IReadOnlyDictionary<string,string> Choices(RuntimeStartup startup)
    {
        var config=startup.Plugins.Single(p=>p.Id is "definition.module" or "definition.state").Configuration;
        if(!config.TryGetProperty("presets",out var presets)) return new Dictionary<string,string>();
        if(presets.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("definition.presets: expected object");
        var result=new Dictionary<string,string>();
        foreach(var item in presets.EnumerateObject())
        {
            if(string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>80 || !result.TryAdd(item.Name,item.Value.GetString()!)) throw new InvalidDataException("Invalid/duplicate definition preset");
            ModulePackage.ValidatePath(result[item.Name]);
        }
        if(result.Count is <1 or >32 || !result.Values.Contains(config.GetProperty("path").GetString())) throw new InvalidDataException("Default definition must be a declared preset");
        return result;
    }
    public static string? Selected(LoadedModule module)
    {
        var source=module.RuntimeIdentity!.DefinitionSource;
        return Choices(module).FirstOrDefault(p=>p.Value==source).Key;
    }
    internal static RuntimeStartup Select(RuntimeStartup startup,string? preset)
    {
        var choices=Choices(startup);
        if(preset==null) return startup;
        if(!choices.TryGetValue(preset,out var source)) throw new InvalidDataException("Unknown definition preset: "+preset);
        return startup with {Plugins=startup.Plugins.Select(p=>
        {
            if(p.Id is not ("definition.module" or "definition.state")) return p;
            var config=JsonNode.Parse(p.Configuration.GetRawText())!.AsObject();config["path"]=source;
            return p with {Configuration=JsonSerializer.SerializeToElement(config)};
        }).ToList()};
    }
}
