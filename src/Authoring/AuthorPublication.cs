using System.Text;
using System.Text.Json.Nodes;

namespace BoardGame.Authoring;

/// <summary>Author-only publication choices. Runtime enablement remains in game.start.json.</summary>
public static class AuthorPublication
{
    public const string FileName="author.publish.json";
    public static JsonObject Read(IAuthorFiles files)=>files.Paths.Contains(FileName)?Parse(files.Text(FileName)):new(){["format"]=1,["providers"]=new JsonObject()};
    public static JsonObject Parse(string text)
    {
        var value=JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException(FileName+": expected object");
        if(value["format"]?.GetValue<int>()!=1 || value["providers"] is not JsonObject providers)
            throw new InvalidDataException(FileName+": expected format 1 and providers object");
        if(value["selected"] is {} selected && selected is not JsonValue)throw new InvalidDataException(FileName+": selected must be a provider ID");
        _=value["selected"]?.GetValue<string>();
        foreach(var provider in providers)
            if(string.IsNullOrWhiteSpace(provider.Key) || provider.Value is not JsonObject)
                throw new InvalidDataException(FileName+": provider parameters must be an object");
        return value;
    }
    public static ProjectChanges Change(ProjectView view,JsonObject settings,string requestId)
    {
        _=Read(view);var text=Parse(settings.ToJsonString()).ToJsonString(new(){WriteIndented=true});
        return view.Change(requestId,new Dictionary<string,byte[]?>{[FileName]=Encoding.UTF8.GetBytes(text)});
    }
}
