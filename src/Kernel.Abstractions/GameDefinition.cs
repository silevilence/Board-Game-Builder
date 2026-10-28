using System.Text.Json;

namespace BoardGame.Kernel;

// Definition loaders return data, never workers, authority objects or UI services.
public sealed record GameDefinition(GameState Initial,
    SortedDictionary<string,string> ResourceHashes,
    SortedDictionary<string,byte[]> ResourceData,
    SortedDictionary<string,JsonElement> CapabilityData,
    ResourcePool? Resources=null);

public sealed record RuntimeIdentity(int Format,RuntimeStartup Startup,string DefinitionSource,
    SortedDictionary<string,string> ContentHashes);
