using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace BoardGame.Kernel;
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters={new OrdinalDictionaryConverterFactory()} };
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty JSON value.");
    public static T Copy<T>(T value) => Read<T>(Json(value));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
// JSON does not carry a dictionary's comparer. Restore ordinal ordering at every wire boundary,
// otherwise mixed-case resource paths change module fingerprints and replay hashes across hosts.
internal sealed class OrdinalDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition()==typeof(SortedDictionary<,>) && type.GetGenericArguments()[0]==typeof(string);
    public override JsonConverter CreateConverter(Type type,JsonSerializerOptions options) =>
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
                if(!values.TryAdd(key,JsonSerializer.Deserialize<T>(ref reader,options)!)) throw new JsonException("Duplicate dictionary key: "+key);
            }
            if(reader.TokenType!=JsonTokenType.EndObject) throw new JsonException("Unterminated dictionary");
            return values;
        }
        public override void Write(Utf8JsonWriter writer,SortedDictionary<string,T> value,JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach(var pair in value.OrderBy(p=>p.Key,StringComparer.Ordinal)) { writer.WritePropertyName(pair.Key); JsonSerializer.Serialize(writer,pair.Value,options); }
            writer.WriteEndObject();
        }
    }
}
public sealed record Seat(string Id, string Team, string Controller = "human");
public sealed record CounterBounds(int Min,int Max);
public sealed class Entity
{
    public string Id { get; set; } = "";
    public string? Owner { get; set; }
    public string Visibility { get; set; } = "public";
    public string? Container { get; set; }
    public bool IsContainer { get; set; }
    public int Capacity { get; set; }
    public bool Ordered { get; set; }
    public List<string> Contents { get; set; } = [];
    public List<string> Faces { get; set; } = [];
    public List<string> FaceResources { get; set; } = [];
    public int Face { get; set; }
    public bool FaceUp { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public SortedDictionary<string, int> Properties { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,CounterBounds> CounterBounds { get; set; } = new(StringComparer.Ordinal);
    public List<int> DiceFaces { get; set; }=[];
}
public sealed class GameState
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public SecretRound? Secret {get;set;}
    public BoardLayout? Layout { get; set; }
    public SortedDictionary<string, Entity> Entities { get; set; } = new(StringComparer.Ordinal);
    public List<Seat> Seats { get; set; } = [];
    public List<string> Phases { get; set; } = [];
    public int CurrentSeat { get; set; }
    public string Phase { get; set; } = "setup";
    public int Turn { get; set; }
    public bool Ended { get; set; }
    public List<string> Winners { get; set; } = [];
    public uint RandomState { get; set; } = 1;
}
public sealed record LoadedModule(string Id, string Version, string Fingerprint, GameState Initial,
    string Rules, string Cpu, string RulesPath, string CpuPath, SortedDictionary<string,string>? ResourceHashes = null,
    SortedDictionary<string,byte[]>? ResourceData = null, SortedDictionary<string,string>? RuntimeHashes = null);
