using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace Example.RuntimePlugin;

// A small external author format for exercising complete preset content discovery.
public static class Document
{
    public static ApprovedApplicationPlugin Create()=>new(_=>new("sample.definition","1",1,["definition"],[],scope=>
        scope.Provide("definition",new Loader(scope.Configuration.GetProperty("path").GetString()!)))
        {Content=new(["path","presets"],Prepare:true,Definition:config=>new(config.GetProperty("path").GetString()!,Presets(config)),SelectSource:Select)});
    private static Dictionary<string,string> Presets(JsonElement config)=>config.TryGetProperty("presets",out var value)
        ?value.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal):new(StringComparer.Ordinal);
    private static JsonElement Select(JsonElement config,string path)
    {
        using var buffer=new MemoryStream();
        using(var writer=new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();writer.WriteString("path",path);
            if(config.TryGetProperty("presets",out var presets)){writer.WritePropertyName("presets");presets.WriteTo(writer);}
            writer.WriteEndObject();
        }
        using var document=JsonDocument.Parse(buffer.ToArray());return document.RootElement.Clone();
    }
    private sealed class Loader(string path):IContentLoader
    {
        public string PayloadFormat=>"sample.document";
        public int PayloadVersion=>1;
        public JsonElement Load(IContentSource source){using var document=JsonDocument.Parse(source.Read(path));return document.RootElement.Clone();}
    }
}
