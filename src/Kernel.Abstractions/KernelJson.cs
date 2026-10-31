using System.Text.Json;
using System.Text.Json.Serialization;
namespace BoardGame.Kernel;

internal sealed record FlowContextInput(SortedDictionary<string,JsonElement> GameContext,SortedDictionary<string,JsonElement> NodeContext);

// Only kernel-owned contracts and their data dependencies belong here.
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(FlowContextInput))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(LoadedModule))]
[JsonSerializable(typeof(ReplayLog))]
[JsonSerializable(typeof(List<ActionOffer>))]
[JsonSerializable(typeof(List<Diagnostic>))]
[JsonSerializable(typeof(List<Submission>))]
[JsonSerializable(typeof(List<RuntimePlugin>))]
[JsonSerializable(typeof(SeatView))]
[JsonSerializable(typeof(RuntimeStartup))]
[JsonSerializable(typeof(KeyValuePair<string,string>[]))]
internal partial class KernelJson:JsonSerializerContext;
