using System.Text.Json.Serialization;
namespace BoardGame.Kernel;

internal sealed record ReplayHeaderInput(string Fingerprint,uint Seed,GameState Initial);
internal sealed record ReplayEntryInput(Submission Submission,List<AppliedCommand> Effects,string StateHash,string PreviousHash);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(ReplayHeaderInput))]
[JsonSerializable(typeof(ReplayEntryInput))]
internal partial class ReplayJson:JsonSerializerContext;
