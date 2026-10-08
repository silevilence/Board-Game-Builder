using System.Text.Json.Nodes;

namespace BoardGame.Editor;

// Import contributions update the startup document and definition in one undoable edit.
internal static class ResourceAuthoring
{
    public static JsonObject AddImages(JsonObject startup,IReadOnlyDictionary<string,string> images)
    {
        var plugins=startup["plugins"]!.AsArray();
        JsonObject Ensure(string id)
        {
            var plugin=plugins.FirstOrDefault(p=>p!["id"]!.GetValue<string>()==id)?.AsObject();
            if(plugin==null)
            {
                plugin=new JsonObject {["id"]=id,["version"]="1",["api"]=1,["configuration"]=new JsonObject {["sources"]=new JsonArray()}};
                plugins.Add(plugin);
            }
            return plugin["configuration"]!.AsObject();
        }
        _=Ensure("resources");var config=Ensure("resources.images");
        if(config["sources"]==null)config["sources"]=new JsonArray();
        foreach(var image in images)config["sources"]!.AsArray().Add(new JsonObject {["path"]=image.Value,["name"]=image.Key});
        return startup;
    }
}
