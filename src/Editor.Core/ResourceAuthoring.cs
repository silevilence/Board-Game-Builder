using System.Text.Json.Nodes;
using BoardGame.Authoring;

namespace BoardGame.Editor;

// Import contributions update the startup document and definition in one undoable edit.
internal static class ResourceAuthoring
{
    public static JsonObject AddImages(JsonObject startup,IReadOnlyDictionary<string,string> images)
    {
        _=ResourceOperations.Ensure(startup,"resources");var config=ResourceOperations.Ensure(startup,"resources.images");
        if(config["sources"]==null)config["sources"]=new JsonArray();
        foreach(var image in images)config["sources"]!.AsArray().Add(new JsonObject {["path"]=image.Value,["name"]=image.Key});
        return startup;
    }
}
