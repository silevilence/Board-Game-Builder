using System.Text.Json;

// Preserve the existing source/JSON contract. These identities contain no board-game model.
namespace BoardGame.Kernel;

public sealed record RuntimePluginSelection(string Id,string Version,int Api,JsonElement Configuration,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Implementation=null);
public sealed record RuntimeStartup(int Format,string Id,string Version,List<RuntimePluginSelection> Plugins);
