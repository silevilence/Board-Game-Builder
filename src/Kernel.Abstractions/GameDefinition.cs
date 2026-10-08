using System.Text.Json;

namespace BoardGame.Kernel;

// Definition loaders return data, never workers, authority objects or UI services.
public sealed record GameDefinition(GameState Initial,
    SortedDictionary<string,string> ResourceHashes,
    SortedDictionary<string,byte[]> ResourceData,
    SortedDictionary<string,JsonElement> CapabilityData);

public sealed record RuntimePluginSelection(string Id,string Version,int Api,JsonElement Configuration);
public sealed record RuntimeStartup(int Format,string Id,string Version,List<RuntimePluginSelection> Plugins);
public sealed record RuntimeIdentity(int Format,RuntimeStartup Startup,string DefinitionSource,
    SortedDictionary<string,string> ContentHashes);
