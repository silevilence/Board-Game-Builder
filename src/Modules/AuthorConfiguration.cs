using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
namespace BoardGame.Modules;

public static class AuthorConfiguration
{
    public static JsonObject FromYaml(string source)
    {
        if(source.Length>4_000_000) throw new InvalidDataException("YAML configuration too large");
        var value=new DeserializerBuilder().WithDuplicateKeyChecking().WithAttemptingUnquotedStringTypeDeserialization().WithMaximumRecursion(64).Build().Deserialize<object>(source);
        return Convert(value,0)?.AsObject() ?? throw new InvalidDataException("Expected YAML mapping");
    }
    private static JsonNode? Convert(object? value,int depth)
    {
        if(depth>48) throw new InvalidDataException("YAML nesting exceeds 48");
        if(value is IDictionary<object,object> map)
        {
            var result=new JsonObject();
            foreach(var entry in map) { if(entry.Key is not string key) throw new InvalidDataException("Expected text mapping key"); result.Add(key,Convert(entry.Value,depth+1)); }
            return result;
        }
        if(value is IEnumerable<object> list) return new JsonArray(list.Select(v=>Convert(v,depth+1)).ToArray());
        return System.Text.Json.JsonSerializer.SerializeToNode(value);
    }
    public static string ToYaml(JsonObject value) => new SerializerBuilder().Build().Serialize(Plain(value));
    private static object? Plain(JsonNode? node) => node switch {
        JsonObject map=>map.ToDictionary(p=>p.Key,p=>Plain(p.Value)),
        JsonArray list=>list.Select(Plain).ToArray(),
        null=>null,
        _=>System.Text.Json.JsonSerializer.Deserialize<object>(node.ToJsonString()) switch {
            System.Text.Json.JsonElement e when e.ValueKind==System.Text.Json.JsonValueKind.String=>e.GetString(),
            System.Text.Json.JsonElement e when e.ValueKind==System.Text.Json.JsonValueKind.Number=>e.GetDouble(),
            System.Text.Json.JsonElement e when e.ValueKind==System.Text.Json.JsonValueKind.True=>true,
            System.Text.Json.JsonElement e when e.ValueKind==System.Text.Json.JsonValueKind.False=>false,
            _=>null } };

    public static JsonObject Expand(JsonObject author)
    {
        var result=(JsonObject)author.DeepClone();
        var prototypes=(author["prototypes"] as JsonArray ?? []).ToDictionary(p=>p!["id"]!.GetValue<string>(),p=>p!.AsObject(),StringComparer.Ordinal);
        foreach(var entity in result["entities"]!.AsArray())
        {
            if(entity!["prototype"] is not {} name) continue;
            if(!prototypes.TryGetValue(name.GetValue<string>(),out var prototype)) throw new InvalidDataException($"{entity["id"]}: unknown prototype {name}");
            var baseComponents=prototype["components"]!.AsArray();
            var overrides=entity["components"]!.AsArray();
            var components=new JsonArray();
            var types=new HashSet<string>();
            foreach(var component in baseComponents)
            {
                var type=component!["type"]!.GetValue<string>();
                if(!types.Add(type)) throw new InvalidDataException("Duplicate prototype component: "+type);
                var matches=overrides.Where(c=>c!["type"]!.GetValue<string>()==type).ToArray();
                if(matches.Length>1) throw new InvalidDataException("Duplicate instance component: "+type);
                var merged=(JsonObject)component.DeepClone();
                if(matches.Length==1) Merge(merged,matches[0]!.AsObject());
                components.Add(merged);
            }
            foreach(var component in overrides) if(!types.Contains(component!["type"]!.GetValue<string>())) components.Add(component.DeepClone());
            entity["components"]=components;
        }
        return result;
    }
    private static void Merge(JsonObject target,JsonObject source)
    {
        foreach(var field in source)
            if(target[field.Key] is JsonObject a && field.Value is JsonObject b) Merge(a,b);
            else target[field.Key]=field.Value?.DeepClone();
    }
}
