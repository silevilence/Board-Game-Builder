using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class MediumTests
{
    public static void Run()
    {
        var doc=ModuleDocument.Create(); doc.EditConfiguration(c=>c["entities"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"die","components":[{"type":"dice","data":{"values":[1,3,5]}},{"type":"counter","data":{"property":"value","min":1,"max":5}},{"type":"properties","data":{"value":1}}]}""")));
        var module=doc.Snapshot(); var a=new GameEngine(module.Initial,23,module.Fingerprint); var b=new GameEngine(module.Initial,23,module.Fingerprint);
        for(var i=0;i<15;i++) { Check.That(a.Submit("p1",new(){Kind="roll",Entity="die"}).Accepted); b.Submit("p1",new(){Kind="roll",Entity="die"}); }
        Check.That(Wire.Json(a.ExportLog())==Wire.Json(b.ExportLog()));
        Check.That(Wire.Json(GameEngine.Replay(module,a.ExportLog()).ExportAuthorityState())==Wire.Json(a.ExportAuthorityState()));
        var before=Wire.Json(a.ExportAuthorityState()); Check.That(!a.Submit("p1",new(){Kind="set",Entity="die",Property="value",Value=6}).Accepted); Check.That(before==Wire.Json(a.ExportAuthorityState()));
        doc.EditConfiguration(c=>c["phases"]=new System.Text.Json.Nodes.JsonArray("first","second")); var engine=new GameEngine(doc.Snapshot().Initial);
        Check.That(engine.Submit("p1",new(){Kind="phase",Phase="second"}).Accepted && engine.Project("p1").Phase=="second");
        Authoring.SetComponent(doc,"entities","die","counter",new(){["property"]="value",["min"]=3,["max"]=1}); Check.Throws(()=>doc.Snapshot(),"counter");
    }
}
