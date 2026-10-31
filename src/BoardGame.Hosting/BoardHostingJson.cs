using System.Text.Json.Serialization;
using BoardGame.Modules;
namespace BoardGame.Runtime;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(BoardApplication.Payload))]
[JsonSerializable(typeof(BoardApplication.Settings))]
[JsonSerializable(typeof(List<RuntimeDependency>))]
internal partial class BoardHostingJson:JsonSerializerContext;
