using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RuntimeStartup))]
[JsonSerializable(typeof(ContentSnapshot))]
[JsonSerializable(typeof(ContentIdentity))]
[JsonSerializable(typeof(ContentPayload))]
[JsonSerializable(typeof(ContentFingerprintInput))]
[JsonSerializable(typeof(GamePackage.PackageManifest))]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(JsonElement))]
internal partial class ContentJsonContext : JsonSerializerContext;

internal sealed record ContentFingerprintInput(ContentIdentity Identity,ContentPayload Payload);
