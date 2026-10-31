using System.Text.Json.Serialization;
namespace BoardGame.Runtime;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(BoardUpdateRequest))]
[JsonSerializable(typeof(BoardUpdateResult))]
public partial class BoardSessionJson:JsonSerializerContext;
