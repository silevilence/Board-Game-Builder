using BoardGame.Authoring;
using System.Text.Json.Nodes;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ReviewFollowupTests
{
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-review-followup-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/state-minimal",directory);
            var current=GameRuntime.Load(directory);
            Check.That(GameRuntime.Load(Path.Combine(directory,GameRuntime.Entry)).Fingerprint==current.Fingerprint,"Selected startup file did not resolve its directory");
            Check.Throws(()=>new PlaySession(current with {RuntimeIdentity=null}),"runtime identity");
            Check.That(GameRuntime.PluginVersions.Count==12 && GameRuntime.PluginVersions.Values.All(v=>v=="1"));
            Check.That(new[]{"resources","resources.images","resources.texturepacker"}.All(GameRuntime.PluginVersions.ContainsKey));
            foreach(var (json,field) in new[]{("null","$"),("[]","$"),("{\"format\":1}","$.state"),
                ("{\"format\":1,\"state\":null,\"resources\":{}}","$.state"),
                ("{\"format\":1,\"state\":{},\"resources\":[]}","$.resources"),
                ("{\"format\":1,\"state\":{},\"resources\":{\"face\":null}}","$.resources.face"),
                ("{\"format\":1,\"state\":{\"seats\":null},\"resources\":{}}","$.state")})
            {
                File.WriteAllText(Path.Combine(directory,"position.json"),json);
                try {GameRuntime.Load(directory);throw new Exception("Malformed state definition was accepted");}
                catch(InvalidDataException error)
                {Check.That(error.Message.Contains("definition.state") && error.Message.Contains("position.json") && error.Message.Contains(field),error.Message);}
            }
        }
        finally {Directory.Delete(directory,true);}

        using var first=new DocumentWorkspace();using var second=new DocumentWorkspace();
        first.Set(ModuleDocument.Open("samples/state-minimal"));second.Set(ModuleDocument.Open("samples/state-minimal"));
        var notifications=0;first.Changed+=()=>notifications++;
        using(var registration=first.AuthorFormats.Register(new("definition.state","1",1,s=>new StateAuthor(s.Configuration.GetProperty("path").GetString()!))))
        {
            Check.That(first.Current!.SupportsStructuredEditing && !second.Current!.SupportsStructuredEditing,"Author format registrations leaked between workspaces");
            Check.Throws(()=>first.AuthorFormats.Register(new("definition.state","1",1,_=>new StateAuthor("position.json"))),"Duplicate author");
            first.Current.EditConfiguration(c=>c["phases"]!.AsArray().Add("review"));
            Check.That(first.Current.Snapshot().Initial.Phases.Contains("review"),"Author serializer was bypassed");
            first.Current.Undo();Check.That(!first.Current.Snapshot().Initial.Phases.Contains("review"));
        }
        Check.That(!first.Current!.SupportsStructuredEditing && notifications>=2,"Author support survived provider disposal");
        Check.That(first.Draft("position.json").Text.Length>0 && first.Current.Snapshot().Id=="state-minimal");

        var doc=ModuleDocument.Create();first.Set(doc);
        doc.EditText("definition","literal file");doc.EditText("module.yaml","literal YAML file");
        Check.That(first.Draft("definition").Text=="literal file" && first.Draft("module.yaml").Text=="literal YAML file","File name was treated as a draft alias");
        var jsonTarget=new DocumentDraftTarget(doc.DefinitionPath);var yamlTarget=new DocumentDraftTarget(doc.DefinitionPath,true);
        first.EditDraft(jsonTarget,first.Draft(jsonTarget).Text+"\n");first.EditDraft(yamlTarget,first.Draft(yamlTarget).Text+"\n");
        Check.Throws(first.ApplyDrafts,"JSON 与 YAML");
        first.DiscardDraft(yamlTarget);first.ApplyDrafts();
        Check.That(doc.Text("module.yaml")=="literal YAML file");
    }
    // This fixture adapts the minimal state's seat/phase author model; production state UI remains optional.
    private sealed record StateAuthor(string DefinitionPath):IGameAuthorFormat
    {
        public JsonObject Read(IAuthorFiles document)
        {
            var state=JsonNode.Parse(document.Text(DefinitionPath))!["state"]!;
            return new() {["seats"]=state["seats"]!.DeepClone(),["phases"]=state["phases"]!.DeepClone()};
        }
        public string Serialize(IAuthorFiles document,JsonObject configuration)
        {
            var root=JsonNode.Parse(document.Text(DefinitionPath))!;
            root["state"]!["seats"]=configuration["seats"]!.DeepClone();root["state"]!["phases"]=configuration["phases"]!.DeepClone();
            return root.ToJsonString();
        }
    }
}
