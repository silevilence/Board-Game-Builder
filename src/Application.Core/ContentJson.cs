using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoardGame.AppRuntime;

internal static class ContentJson
{
    internal static readonly JsonSerializerOptions Options=new()
    {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    internal static T Read<T>(ReadOnlySpan<byte> data)
    {
        using var json=JsonDocument.Parse(data.ToArray());
        CheckUnique(json.RootElement);
        return json.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException("Empty content JSON");
    }
    internal static string Write<T>(T value)=>JsonSerializer.Serialize(value,Options);
    internal static T Copy<T>(T value)=>Read<T>(Encoding.UTF8.GetBytes(Write(value)));
    internal static string Hash(ReadOnlySpan<byte> data)=>Convert.ToHexString(SHA256.HashData(data));
    internal static string Canonical<T>(T value)
    {
        var json=JsonSerializer.SerializeToElement(value,Options);
        using var buffer=new MemoryStream();
        using(var writer=new Utf8JsonWriter(buffer)) Sort(json,writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    private static void Sort(JsonElement value,Utf8JsonWriter writer)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach(var property in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)) {writer.WritePropertyName(property.Name);Sort(property.Value,writer);}
            writer.WriteEndObject();
        }
        else if(value.ValueKind==JsonValueKind.Array)
        {writer.WriteStartArray();foreach(var item in value.EnumerateArray()) Sort(item,writer);writer.WriteEndArray();}
        else value.WriteTo(writer);
    }
    private static void CheckUnique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            HashSet<string> names=[];
            foreach(var property in value.EnumerateObject())
            {if(!names.Add(property.Name)) throw new InvalidDataException(property.Name+": duplicate JSON key");CheckUnique(property.Value);}
        }
        else if(value.ValueKind==JsonValueKind.Array) foreach(var item in value.EnumerateArray()) CheckUnique(item);
    }
}
