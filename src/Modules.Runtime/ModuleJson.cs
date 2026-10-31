using System.Text.Json.Serialization;
using BoardGame.Kernel;
namespace BoardGame.Modules;

internal sealed record ModuleFingerprintInput(string Id,string Version,GameState State,string Rules,string Cpu,SortedDictionary<string,string> Resources,SortedDictionary<string,string>? Runtime);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(ModuleFingerprintInput))]
internal partial class ModuleJson:JsonSerializerContext;
