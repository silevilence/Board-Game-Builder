using System.Text.Json;

// Preserve the existing source/JSON contract. These identities contain no board-game model.
namespace BoardGame.Kernel;

public sealed record RuntimePluginSelection(string Id,string Version,int Api,JsonElement Configuration);
public sealed record RuntimeStartup(int Format,string Id,string Version,List<RuntimePluginSelection> Plugins);
