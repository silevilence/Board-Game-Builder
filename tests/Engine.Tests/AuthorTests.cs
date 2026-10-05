using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class AuthorTests
{
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
        Authoring.SetComponent(doc,"entities","one","properties",new() {["score"]=9});
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
            doc.EditConfiguration(c=>c["version"]="2"); Check.That(doc.Dirty);
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
        }
        finally { if(Directory.Exists(path)) Directory.Delete(path,true); }
    }
}
