using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace Engine.Tests;

public static class DocumentTests
{
    public static void Runtime()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-documents-"+Guid.NewGuid());TestModules.CopyGame("samples/state-minimal",folder);
        try
        {
            var state=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"position.json")))!;
            state["state"]!["entities"]=JsonNode.Parse("""{"public":{"id":"public","properties":{"count":2}},"private":{"id":"private","visibility":"hidden","properties":{"count":99}}}""");
            File.WriteAllText(Path.Combine(folder,"position.json"),state.ToJsonString());
            File.Copy("samples/freecell/assets/card_clubs_A.png",Path.Combine(folder,"paper.png"));
            var startup=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,GameRuntime.Entry)))!;
            foreach(var (id,config) in new[]{("resources","{}"),("resources.images","""{"sources":[{"path":"paper.png","name":"paper"}]}"""),("resources.documents","""{"path":"pages.json"}""")})
                startup["plugins"]!.AsArray().Add(new JsonObject{["id"]=id,["version"]="1",["api"]=1,["configuration"]=JsonNode.Parse(config)});
            File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),startup.ToJsonString());
            var library=new DocumentLibrary(1,[new("ledger","Ledger",[new("First","paper",600,900,[new(10,20,Text:"Count: {value}",Binding:new("public","count")),new(10,40,Binding:new("private","count"))])],true)]);
            File.WriteAllText(Path.Combine(folder,"pages.json"),Wire.Json(library));
            var module=GameRuntime.Load(folder);GameRuntime.ValidateSnapshot(Wire.Copy(module));
            var view=new GameEngine(module.Initial).Project("p1");var fields=library.Panels[0].Pages[0].Fields;
            Check.That(DocumentProjection.Text(fields[0],view)=="Count: 2");
            Check.That(DocumentProjection.Text(fields[1],view)=="","Hidden state leaked into a document");
            Check.That(DocumentProjection.Text(fields[0] with {Binding=new("public","count",NonzeroText:"✓")},view)=="Count: ✓");
            var package=Path.Combine(folder,"game.bgm");GamePackage.Pack(folder,package);Check.That(GameRuntime.Load(package).Fingerprint==module.Fingerprint);
            var selected=module.Resources!.Select([]);Check.That(selected.Images.ContainsKey("paper")&&selected.Documents!=null,"Pages missing in projected resource selection");
            Check.Throws(()=>DocumentFile.Parse(Wire.Json(library with {Format=2})),"format");
            var invalid=library with {Panels=[library.Panels[0] with {Pages=[library.Panels[0].Pages[0] with {Fields=[new(1,2,Binding:new("public","missing"))]}]}]};
            File.WriteAllText(Path.Combine(folder,"pages.json"),Wire.Json(invalid));Check.Throws(()=>GameRuntime.Load(folder),"unknown binding");
        }
        finally {Directory.Delete(folder,true);}
    }
}
