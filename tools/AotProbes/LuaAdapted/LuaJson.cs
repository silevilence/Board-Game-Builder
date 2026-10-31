using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BoardGame.Lua;

internal static class LuaJson
{
    internal static string Json<T>(T value)=>JsonSerializer.Serialize(value,Type<T>());
    internal static T Read<T>(string value)=>JsonSerializer.Deserialize(value,Type<T>()) ?? throw new InvalidDataException("Empty Lua payload");
    private static JsonTypeInfo<T> Type<T>()=>(JsonTypeInfo<T>)(LuaJsonContext.Default.GetTypeInfo(typeof(T)) ?? throw new InvalidDataException("Unregistered Lua protocol type"));

    // LuaExecution already validates depth, size, cycles and allowed types.
    // This writer preserves that closed data graph without runtime metadata.
    internal static string Result(object? result)
    {
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) Write(writer,result);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void Write(Utf8JsonWriter writer,object? value)
    {
        switch(value)
        {
            case null:writer.WriteNullValue();break;
            case bool boolean:writer.WriteBooleanValue(boolean);break;
            case double number:writer.WriteNumberValue(number);break;
            case string text:writer.WriteStringValue(text);break;
            case List<object?> array:
                writer.WriteStartArray();foreach(var child in array) Write(writer,child);writer.WriteEndArray();break;
            case Dictionary<string,object?> fields:
                writer.WriteStartObject();foreach(var field in fields) {writer.WritePropertyName(field.Key);Write(writer,field.Value);}writer.WriteEndObject();break;
            default:throw new InvalidDataException("Unexpected Lua result type");
        }
    }
}

public sealed record LuaCallShape(bool ArrayResult=false,string[]? ArrayFields=null,string[]? ValidateExports=null);

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(LuaRequest))]
[JsonSerializable(typeof(LuaResponse))]
internal partial class LuaJsonContext:JsonSerializerContext;
