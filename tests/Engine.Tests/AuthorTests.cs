using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class AuthorTests
{
    public static void Layouts()
    {
        foreach(var kind in new[]{"free","square","hex"})
        {
            var doc=ModuleDocument.Create(); doc.EditConfiguration(c=> { c["layout"]=System.Text.Json.Nodes.JsonNode.Parse(Wire.Json(new BoardLayout(kind))); c["entities"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"pawn","components":[{"type":"position","data":{"x":2,"y":3}}]}""")); });
            var module=doc.Snapshot(); var grid=module.Initial.Layout!; var world=grid.World(2,3); var logical=grid.Logical(world.X,world.Y); Check.That(Math.Abs(logical.X-2)<1e-8 && Math.Abs(logical.Y-3)<1e-8);
            var engine=new GameEngine(module.Initial,42,module.Fingerprint); Check.That(engine.Submit("p1",new(){Kind="move",Entity="pawn",X=2.3,Y=3.1}).Accepted);
            var moved=engine.Project("p1").Entities[0]; var expected=grid.Snap(2.3,3.1); Check.That(moved.X==expected.X && moved.Y==expected.Y);
            var scene=BoardGame.View.TableScene.From(engine.Project("p1")); var item=scene.Items.Single();
            var target=grid.World(moved.X+1,moved.Y); var origin=grid.World(moved.X,moved.Y); var drag=item.MoveBy((float)(target.X-origin.X),(float)(target.Y-origin.Y));
            Check.That(Math.Abs(drag.X-moved.X-1)<1e-5 && Math.Abs(drag.Y-moved.Y)<1e-5);
            var replay=GameEngine.Replay(module,engine.ExportLog()); Check.That(Wire.Json(replay.ExportAuthorityState())==Wire.Json(engine.ExportAuthorityState()));
            Check.That(grid.Neighbors(0,0).Count==(kind=="hex"?6:kind=="square"?4:0));
        }
    }
    public static void Media()
    {
        var doc=ModuleDocument.Open("samples/quick-cards"); var original=doc.Snapshot();
        foreach(var entity in doc.Configuration["entities"]!.AsArray().ToArray())
            foreach(var component in entity!["components"]!.AsArray().ToArray())
                Authoring.SetComponent(doc,"entities",entity["id"]!.GetValue<string>(),component!["type"]!.GetValue<string>(),component["data"]!.AsObject());
        Check.That(doc.Snapshot().Fingerprint==original.Fingerprint,"Inspector reconstruction differs");
        Authoring.SetComponent(doc,"entities","deck","container",new(){["capacity"]=1,["ordered"]=true,["contents"]=new System.Text.Json.Nodes.JsonArray("absent")});
        Check.Throws(()=>doc.Snapshot(),"invalid reference"); doc.Undo();
        Authoring.SetComponent(doc,"entities","deck","container",new(){["capacity"]=1,["ordered"]=true,["contents"]=original.Initial.Entities["deck"].Contents.Aggregate(new System.Text.Json.Nodes.JsonArray(),(a,s)=>{a.Add(s);return a;})});
        Check.Throws(()=>doc.Snapshot(),"capacity"); doc.Undo();
        Authoring.SetComponent(doc,"entities","deck","visibility",new(){["mode"]="everyone"}); Check.Throws(()=>doc.Snapshot(),"visibility");
    }
    public static void Prototypes()
    {
        var doc=ModuleDocument.Create(); Authoring.CreatePrototype(doc,"pawn");
        Authoring.SetComponent(doc,"prototypes","pawn","properties",new() {["score"]=1,["health"]=5});
        Authoring.Instantiate(doc,"pawn","one"); Authoring.Instantiate(doc,"pawn","two");
        Authoring.ApplyFields(doc,"entities","one","properties",new() {["score"]=1,["health"]=5},new() {["score"]=9,["health"]=5});
        Authoring.SetComponent(doc,"prototypes","pawn","properties",new() {["score"]=2,["health"]=7});
        var state=doc.Snapshot(); Check.That(state.Initial.Entities["one"].Properties["score"]==9 && state.Initial.Entities["two"].Properties["score"]==2 && state.Initial.Entities["one"].Properties["health"]==7);
        var yaml=BoardGame.Modules.AuthorConfiguration.ToYaml(doc.Configuration); Authoring.ApplyYaml(doc,yaml);
        Check.That(doc.Snapshot().Fingerprint==state.Fingerprint,"YAML changed semantic types");
        Check.Throws(()=>Authoring.ApplyYaml(doc,"id: a\nid: b"));
        var path=Path.Combine(Path.GetTempPath(),"bgb-prototype-"+Guid.NewGuid());
        try { doc.Save(path); Check.That(ModuleDocument.Open(path).Snapshot().Fingerprint==state.Fingerprint); }
        finally { if(Directory.Exists(path)) Directory.Delete(path,true); }
    }
    public static void Documents()
    {
        var path=Path.Combine(Path.GetTempPath(),"bgb-document-test-"+Guid.NewGuid());
        try
        {
            var doc=ModuleDocument.Create(); Check.That(doc.Dirty);
            doc.Save(path); Check.That(!doc.Dirty);
            var original=doc.Snapshot().Fingerprint;
            doc.EditStartup(c=>c["version"]="2"); Check.That(doc.Dirty);
            doc.Undo(); Check.That(!doc.Dirty && doc.Snapshot().Fingerprint==original);
            doc.Redo(); var edited=doc.Snapshot(); doc.Save();
            Check.That(ModuleDocument.Open(path).Snapshot().Fingerprint==edited.Fingerprint);
            edited.Initial.Seats.Clear(); Check.That(doc.Snapshot().Initial.Seats.Count==1);
            doc.EditConfiguration(c=>c["phases"]=new System.Text.Json.Nodes.JsonArray());
            Check.Throws(()=>doc.Save(),"phases");
            Check.That(ModuleDocument.Open(path).Snapshot().Version=="2","Invalid save replaced good data");
            doc.Undo(); using var session=new BoardGame.Runtime.PlaySession(doc.Snapshot());
            session.SubmitHuman("p1",new() {Kind="finish"});
            Check.That(!doc.Snapshot().Initial.Ended,"Play changed author state");
            File.WriteAllText(Path.Combine(path,"outside.txt"),"external change");
            doc.EditStartup(c=>c["version"]="3");
            Check.Throws(()=>doc.Save(),"changed outside");
            Check.That(File.ReadAllText(Path.Combine(path,"outside.txt"))=="external change" && ModuleDocument.Open(path).Snapshot().Version=="2","External edit was overwritten");
        }
        finally { if(Directory.Exists(path)) Directory.Delete(path,true); }
    }
}
