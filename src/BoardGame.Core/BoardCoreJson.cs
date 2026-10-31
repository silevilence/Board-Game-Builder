using System.Text.Json.Serialization;
namespace BoardGame.Runtime;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(LuaScript))]
[JsonSerializable(typeof(List<BoardGame.Kernel.RuntimePlugin>))]
internal partial class BoardCoreJson:JsonSerializerContext;
