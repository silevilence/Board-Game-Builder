using System.Text.Json.Serialization;
namespace BoardGame.Kernel;
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
    public FlowState? Flow { get; set; }
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
    string Rules="", string Cpu="", string RulesPath="", string CpuPath="", SortedDictionary<string,string>? ResourceHashes = null,
    SortedDictionary<string,byte[]>? ResourceData = null, SortedDictionary<string,string>? RuntimeHashes = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] List<RuntimePlugin>? RuntimePlugins=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] RuntimeIdentity? RuntimeIdentity=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] GameDefinition? Definition=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ResourcePool? Resources=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SortedDictionary<string,byte[]>? FlowFiles=null);
