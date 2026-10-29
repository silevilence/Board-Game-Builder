using System.Text.Json.Nodes;
using BoardGame.Authoring;

namespace BoardGame.Editor;

// Import contributions update the startup document and definition in one undoable edit.
internal static class ResourceAuthoring
{
    public static JsonObject AddImages(JsonObject startup,IReadOnlyDictionary<string,string> images)
    {
        return ResourceOperations.AddImages(startup,images);
    }
}
