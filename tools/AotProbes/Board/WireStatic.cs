using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace BoardGame.Kernel;

// Single-assembly experiment: production must keep codecs within owning layers.
public static class Wire
{
    public static readonly JsonSerializerOptions Options=CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options=new JsonSerializerOptions
        {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
            NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,
            TypeInfoResolver=BoardJsonContext.Default
        };
        options.Converters.Add(new OrdinalDictionaryConverter<string>());
        options.Converters.Add(new OrdinalDictionaryConverter<int>());
        options.Converters.Add(new OrdinalDictionaryConverter<int?>());
        options.Converters.Add(new OrdinalDictionaryConverter<byte[]>());
        options.Converters.Add(new OrdinalDictionaryConverter<JsonElement>());
        options.Converters.Add(new OrdinalDictionaryConverter<Entity>());
        options.Converters.Add(new OrdinalDictionaryConverter<CounterBounds>());
        options.MakeReadOnly();return options;
    }
    public static JsonTypeInfo<T> Type<T>()=>(JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
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
internal sealed record FlowContextInput(SortedDictionary<string,JsonElement> GameContext,SortedDictionary<string,JsonElement> NodeContext);
internal sealed record ReplayHeaderInput(string Fingerprint,uint Seed,GameState Initial);
internal sealed record ReplayEntryInput(Submission Submission,List<AppliedCommand> Effects,string StateHash,string PreviousHash);
internal sealed record ModuleFingerprintInput(string Id,string Version,GameState State,string Rules,string Cpu,SortedDictionary<string,string> Resources,SortedDictionary<string,string>? Runtime);
internal sealed record LegacyRecordingInput(ReplayLog Log,List<ScriptSwitch> Switches,Dictionary<string,LoadedModule> Versions,string Hash);

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(FlowContextInput))]
[JsonSerializable(typeof(ReplayHeaderInput))]
[JsonSerializable(typeof(ReplayEntryInput))]
[JsonSerializable(typeof(ModuleFingerprintInput))]
[JsonSerializable(typeof(LegacyRecordingInput))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(LoadedModule))]
[JsonSerializable(typeof(SessionRecording))]
[JsonSerializable(typeof(BoardApplication.Payload))]
[JsonSerializable(typeof(BoardUpdateRequest))]
[JsonSerializable(typeof(List<ActionOffer>))]
[JsonSerializable(typeof(List<Diagnostic>))]
[JsonSerializable(typeof(List<RuntimeDependency>))]
[JsonSerializable(typeof(LuaScript))]
[JsonSerializable(typeof(List<RuntimePlugin>))]
[JsonSerializable(typeof(SeatView))]
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(RuntimeStartup))]
internal partial class BoardJsonContext:JsonSerializerContext;
