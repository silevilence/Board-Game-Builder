using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoardGame.Runtime;

internal static class DefinitionContent
{
    private static readonly string[] BoardCapabilities=[..AuthorityPlugin.Required,"rules","session","recording"];
    internal static PluginContentContract Contract(bool presets)=>new(presets?["path","presets"]:["path"],
        RequiredCapabilities:BoardCapabilities,Prepare:true,Definition:config=>Describe(config,presets),
        SelectSource:presets?ReplaceSource:null);
    private static DefinitionSources Describe(JsonElement config,bool presets)
    {
        var source=config.GetProperty("path").GetString()!;ContentPath.Validate(source);
        var result=new Dictionary<string,string>();
        if(presets && config.TryGetProperty("presets",out var values))
        {
            if(values.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("definition.presets: expected object");
            foreach(var item in values.EnumerateObject())
            {
                if(string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>80 || !result.TryAdd(item.Name,item.Value.GetString()!)) throw new InvalidDataException("Invalid/duplicate definition preset");
                ContentPath.Validate(result[item.Name]);
            }
            if(result.Count is <1 or >32 || !result.Values.Contains(source)) throw new InvalidDataException("Default definition must be a declared preset");
        }
        return new(source,result);
    }
    private static JsonElement ReplaceSource(JsonElement config,string source)
    {
        var node=JsonNode.Parse(config.GetRawText())!.AsObject();node["path"]=source;
        return JsonSerializer.SerializeToElement(node);
    }
}
