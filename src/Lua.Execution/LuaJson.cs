using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("Lua")]
[assembly:InternalsVisibleTo("Engine.Tests")]

namespace BoardGame.Lua;

internal static class LuaJson
{
    internal static readonly JsonSerializerOptions Options=new()
    {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    internal static string Json<T>(T value)=>JsonSerializer.Serialize(value,Options);
    internal static T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,Options) ?? throw new InvalidDataException("Empty Lua payload");
}
public sealed record LuaCallShape(bool ArrayResult=false,string[]? ArrayFields=null,string[]? ValidateExports=null);
