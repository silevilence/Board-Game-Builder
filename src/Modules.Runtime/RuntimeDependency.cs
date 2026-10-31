using System.Security.Cryptography;
using System.Text.Json.Serialization;
namespace BoardGame.Modules;

public sealed record RuntimeDependency(string Id,string Version,int Api,[property:JsonPropertyName("editor_only")] bool EditorOnly=false,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Kind=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? File=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Sha256=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] List<string>? Permissions=null);

public static class ModuleHash
{
    public static string Compute(ReadOnlySpan<byte> data)=>Convert.ToHexString(SHA256.HashData(data));
}
