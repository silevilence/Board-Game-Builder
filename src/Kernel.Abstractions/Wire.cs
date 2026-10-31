using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Runtime.CompilerServices;

namespace BoardGame.Kernel;

// Each optional layer supplies its own source-generated metadata explicitly.
public static class Wire
{
    public static readonly JsonSerializerOptions Options=CreateOptions();
    public static JsonSerializerOptions CreateOptions(IJsonTypeInfoResolver? resolver=null)
    {
        var options=new JsonSerializerOptions
        {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
            NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,
            TypeInfoResolver=resolver ?? DefaultResolver()
        };
        options.Converters.Add(new OrdinalDictionaryConverter<string>());
        options.Converters.Add(new OrdinalDictionaryConverter<int>());
        options.Converters.Add(new OrdinalDictionaryConverter<int?>());
        options.Converters.Add(new OrdinalDictionaryConverter<byte[]>());
        options.Converters.Add(new OrdinalDictionaryConverter<JsonElement>());
        options.Converters.Add(new OrdinalDictionaryConverter<Entity>());
        options.Converters.Add(new OrdinalDictionaryConverter<CounterBounds>());
        if(RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault) options.Converters.Add(new OrdinalDictionaryConverterFactory());
        options.MakeReadOnly();return options;
    }
    private static readonly ConditionalWeakTable<JsonSerializerContext,JsonSerializerOptions> contexts=new();
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming","IL2026",Justification="JSON reflection is guarded by its link-time feature switch and dynamic-code support; native/trimmed hosts use KernelJson. See native-runtime-integration validation.")]
    private static IJsonTypeInfoResolver DefaultResolver()
    {
        if(RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault) return new DefaultJsonTypeInfoResolver();
        return KernelJson.Default;
    }
    public static JsonTypeInfo<T> Type<T>(JsonSerializerContext? context=null)=>(JsonTypeInfo<T>)(context==null?Options:contexts.GetValue(context,c=>CreateOptions(c))).GetTypeInfo(typeof(T));
    public static string Json<T>(T value,JsonTypeInfo<T> type)=>JsonSerializer.Serialize(value,type);
    public static T Read<T>(string value,JsonTypeInfo<T> type)=>JsonSerializer.Deserialize(value,type) ?? throw new InvalidDataException("Empty JSON value.");
    public static T Copy<T>(T value,JsonTypeInfo<T> type)=>Read(Json(value,type),type);
    public static string Json<T>(T value)=>JsonSerializer.Serialize(value,Type<T>());
    public static T Read<T>(string value)=>JsonSerializer.Deserialize(value,Type<T>()) ?? throw new InvalidDataException("Empty JSON value.");
    public static T Copy<T>(T value)=>Read<T>(Json(value));
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static JsonElement Element<T>(T value)=>JsonSerializer.SerializeToElement(value,Type<T>());
    public static JsonElement Empty(){using var document=JsonDocument.Parse("{}");return document.RootElement.Clone();}
}
internal sealed class OrdinalDictionaryConverter<T>:JsonConverter<SortedDictionary<string,T>>
{
    public override SortedDictionary<string,T> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        if(reader.TokenType!=JsonTokenType.StartObject)throw new JsonException("Expected dictionary object");
        var values=new SortedDictionary<string,T>(StringComparer.Ordinal);
        while(reader.Read() && reader.TokenType!=JsonTokenType.EndObject)
        {
            if(reader.TokenType!=JsonTokenType.PropertyName)throw new JsonException("Expected dictionary key");
            var key=reader.GetString()!;if(!reader.Read())throw new JsonException("Missing dictionary value");
            if(!values.TryAdd(key,JsonSerializer.Deserialize(ref reader,(JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))!))throw new JsonException("Duplicate dictionary key: "+key);
        }
        if(reader.TokenType!=JsonTokenType.EndObject)throw new JsonException("Unterminated dictionary");
        return values;
    }
    public override void Write(Utf8JsonWriter writer,SortedDictionary<string,T> value,JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach(var pair in value.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {writer.WritePropertyName(pair.Key);JsonSerializer.Serialize(writer,pair.Value,(JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));}
        writer.WriteEndObject();
    }
}
