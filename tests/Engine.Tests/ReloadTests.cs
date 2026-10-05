using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class ReloadTests
{
    public static void Run()
    {
        var doc=ModuleDocument.Open("samples/quick-cards"); var module=doc.Snapshot();
        using var play=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        Check.That(play.StepCpu()); var before=play.AuthorityStateHash();
        doc.EditText("rules.lua","broken lua ?"); Check.Throws(()=>play.ReloadScripts(doc.Snapshot())); Check.That(play.AuthorityStateHash()==before); doc.Undo();
        doc.EditText("rules.lua",doc.Text("rules.lua").Replace("Play ","Changed ")+"\n-- new rules version");
        play.ReloadScripts(doc.Snapshot()); Check.That(play.ExportRecording().Switches.Count==2);
        doc.EditText("cpu.lua","return {decide=function(ctx) return ctx.actions[#ctx.actions].command end}"); play.ReloadScripts(doc.Snapshot());
        while(!play.Ended && play.Fault==null) play.StepCpu(); Check.That(play.Ended);
        var recording=play.ExportRecording(); Check.That(Wire.Hash(Wire.Json(recording.Replay().ExportAuthorityState()))==play.AuthorityStateHash());
        var missing=Wire.Copy(recording); missing.Versions.Remove(missing.Switches[1].Fingerprint); Check.Throws(()=>missing.Replay(),"Missing script version");
        using var fresh=new PlaySession(module); doc.EditConfiguration(c=>c["phases"]=new System.Text.Json.Nodes.JsonArray("different")); Check.Throws(()=>fresh.ReloadScripts(doc.Snapshot()),"restart required");
    }
}
