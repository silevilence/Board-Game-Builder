using System.Text.Json.Nodes;
using BoardGame.Modules;
namespace BoardGame.Editor;

public static class Authoring
{
    public static void ApplyYaml(ModuleDocument doc,string yaml) => doc.EditText("module.json",AuthorConfiguration.FromYaml(yaml).ToJsonString(new() {WriteIndented=true}));
    public static void CreatePrototype(ModuleDocument doc,string id,string? fromEntity=null) => doc.EditConfiguration(root=> {
        ValidateId(id);
        var prototypes=root["prototypes"] as JsonArray ?? new JsonArray();
        if(root["prototypes"]==null) root["prototypes"]=prototypes;
        if(prototypes.Any(p=>p!["id"]!.GetValue<string>()==id)) throw new InvalidDataException("Prototype already exists: "+id);
        var components=fromEntity==null?new JsonArray():Find(AuthorConfiguration.Expand(root),"entities",fromEntity)["components"]!.DeepClone();
        prototypes.Add(new JsonObject { ["id"]=id,["components"]=components });
    });
    public static void Instantiate(ModuleDocument doc,string prototype,string id) => doc.EditConfiguration(root=> {
        ValidateId(id); _=Find(root,"prototypes",prototype);
        if(root["entities"]!.AsArray().Any(e=>e!["id"]!.GetValue<string>()==id)) throw new InvalidDataException("Entity already exists: "+id);
        root["entities"]!.AsArray().Add(new JsonObject { ["id"]=id,["prototype"]=prototype,["components"]=new JsonArray() });
    });
    public static JsonObject Find(JsonObject root,string collection,string id) => root[collection]!.AsArray().FirstOrDefault(e=>e!["id"]!.GetValue<string>()==id)?.AsObject() ?? throw new InvalidDataException($"{collection}: unknown {id}");
    public static void SetComponent(ModuleDocument doc,string collection,string id,string type,JsonObject data) => doc.EditConfiguration(root=> {
        var components=Find(root,collection,id)["components"]!.AsArray();
        var component=components.FirstOrDefault(c=>c!["type"]!.GetValue<string>()==type);
        if(component!=null) component["data"]=data.DeepClone();
        else components.Add(new JsonObject { ["type"]=type,["data"]=data.DeepClone() });
    });
    public static void ApplyFields(ModuleDocument doc,string collection,string id,string type,JsonObject? displayed,JsonObject edited)
    {
        if(displayed==null) { SetComponent(doc,collection,id,type,edited); return; }
        var own=Find(doc.Configuration,collection,id)["components"]!.AsArray().FirstOrDefault(c=>c!["type"]!.GetValue<string>()==type)?["data"]?.DeepClone().AsObject() ?? new JsonObject();
        var changed=false;
        var current=Find(AuthorConfiguration.Expand(doc.Configuration),collection,id)["components"]!.AsArray().FirstOrDefault(c=>c!["type"]!.GetValue<string>()==type)?["data"]?.AsObject();
        foreach(var field in edited)
            if(!JsonNode.DeepEquals(displayed[field.Key],field.Value))
            {
                if(!JsonNode.DeepEquals(current?[field.Key],displayed[field.Key]) && !JsonNode.DeepEquals(current?[field.Key],field.Value))
                    throw new InvalidOperationException("其他面板已修改字段，请重新读取并合并："+field.Key);
                own[field.Key]=field.Value?.DeepClone();changed=true;
            }
        if(changed) SetComponent(doc,collection,id,type,own);
    }
    private static void ValidateId(string id) { if(string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Identity is required"); }
}
