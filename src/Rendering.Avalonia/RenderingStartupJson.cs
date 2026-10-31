using System.Text.Json.Serialization;
using BoardGame.Kernel;
namespace BoardGame.Rendering;

[JsonSerializable(typeof(RuntimeStartup))]
internal partial class RenderingStartupJson:JsonSerializerContext;
