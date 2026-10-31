using System.Text.Json.Serialization;
using BoardGame.Kernel;
namespace BoardGame.Runtime;

/// <summary>Authority recording codec owned by the optional recording implementation.</summary>
public static class RecordingCodec
{
    public static string Write(SessionRecording value)=>Wire.Json(value,Wire.Type<SessionRecording>(RecordingJson.Default));
    public static SessionRecording Read(string json)=>Wire.Read(json,Wire.Type<SessionRecording>(RecordingJson.Default));
    public static SessionRecording Copy(SessionRecording value)=>Read(Write(value));
}
internal sealed record LegacyRecordingInput(ReplayLog Log,List<ScriptSwitch> Switches,Dictionary<string,LoadedModule> Versions,string Hash);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(SessionRecording))]
[JsonSerializable(typeof(LegacyRecordingInput))]
internal partial class RecordingJson:JsonSerializerContext;
