using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace BoardGame.Kernel;
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty JSON value.");
    public static T Copy<T>(T value) => Read<T>(Json(value));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
public sealed record Seat(string Id, string Team, string Controller = "human");
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
    public int Face { get; set; }
    public bool FaceUp { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public SortedDictionary<string, int> Properties { get; set; } = new(StringComparer.Ordinal);
}
public sealed class GameState
{
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
    string Rules, string Cpu, string RulesPath, string CpuPath, SortedDictionary<string,string>? ResourceHashes = null);
