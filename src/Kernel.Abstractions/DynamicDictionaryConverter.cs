using System.Text.Json;
using System.Text.Json.Serialization;
namespace BoardGame.Kernel;
// JSON does not carry a dictionary's comparer. Restore ordinal ordering at every wire boundary,
// otherwise mixed-case resource paths change module fingerprints and replay hashes across hosts.
internal sealed class OrdinalDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition()==typeof(SortedDictionary<,>) && type.GetGenericArguments()[0]==typeof(string);
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming","IL2026",Justification="Legacy converter is enabled only with JSON reflection and dynamic-code support; native/trimmed hosts use closed converters. See native-runtime-integration validation.")]
    public override JsonConverter CreateConverter(Type type,JsonSerializerOptions options)
    {
        if(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault) return CreateDynamic(type);
        throw new NotSupportedException("This dictionary value type requires an explicit static converter.");
    }
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Ordinary author codecs support user-defined dictionary values.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Ordinary author codecs require reflection metadata.")]
    private static JsonConverter CreateDynamic(Type type) =>
        (JsonConverter)Activator.CreateInstance(typeof(OrdinalDictionaryConverter<>).MakeGenericType(type.GetGenericArguments()[1]))!;
    private sealed class OrdinalDictionaryConverter<T> : JsonConverter<SortedDictionary<string,T>>
    {
        public override SortedDictionary<string,T> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            if(reader.TokenType!=JsonTokenType.StartObject) throw new JsonException("Expected dictionary object");
            var values=new SortedDictionary<string,T>(StringComparer.Ordinal);
            while(reader.Read() && reader.TokenType!=JsonTokenType.EndObject)
            {
                if(reader.TokenType!=JsonTokenType.PropertyName) throw new JsonException("Expected dictionary key");
                var key=reader.GetString()!; if(!reader.Read()) throw new JsonException("Missing dictionary value");
                if(!values.TryAdd(key,JsonSerializer.Deserialize(ref reader,(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))!)) throw new JsonException("Duplicate dictionary key: "+key);
            }
            if(reader.TokenType!=JsonTokenType.EndObject) throw new JsonException("Unterminated dictionary");
            return values;
        }
        public override void Write(Utf8JsonWriter writer,SortedDictionary<string,T> value,JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach(var pair in value.OrderBy(p=>p.Key,StringComparer.Ordinal)) { writer.WritePropertyName(pair.Key); JsonSerializer.Serialize(writer,pair.Value,(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))); }
            writer.WriteEndObject();
        }
    }
}
