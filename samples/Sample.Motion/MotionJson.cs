using System.Text.Json.Serialization;
namespace Sample.Motion;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true)]
[JsonSerializable(typeof(MotionSettings))]
[JsonSerializable(typeof(MotionPayload))]
[JsonSerializable(typeof(MotionKey))]
[JsonSerializable(typeof(string))]
internal partial class MotionJson:JsonSerializerContext;
