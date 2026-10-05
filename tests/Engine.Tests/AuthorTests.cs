using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class AuthorTests
{
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
