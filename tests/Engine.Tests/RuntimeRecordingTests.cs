using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class RuntimeRecordingTests
{
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-recording-"+Guid.NewGuid());
        try
        {
            GameMigration.Migrate("samples/quick-cards",directory);
            var module=GameRuntime.Load(directory);using var session=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
            Check.That(session.StepCpu());
            File.AppendAllText(Path.Combine(directory,"rules.lua"),"\n-- reloaded version\n");
            var changed=GameRuntime.Load(directory);Check.That(changed.Fingerprint!=module.Fingerprint);
            session.ReloadScripts(changed);Check.That(session.StepCpu());
            var recording=session.ExportRecording();Check.That(recording.Format==2 && recording.Versions.Count==2);
            Check.That(Wire.Hash(Wire.Json(recording.Replay().ExportAuthorityState()))==session.AuthorityStateHash());
            var path=Path.Combine(directory,"snapshot.json");File.WriteAllText(path,Wire.Json(changed));
            Check.That(GameRuntime.Load(path).Fingerprint==changed.Fingerprint);
            var broken=changed with {RuntimeIdentity=changed.RuntimeIdentity! with {Startup=changed.RuntimeIdentity.Startup with {Version="changed"}}};
            Check.That(ModuleSnapshot.Fingerprint(broken)!=changed.Fingerprint);
            Check.Throws(()=>new PlaySession(broken),"identity");
            Check.Throws(()=>(recording with {Format=999}).Replay(),"format");
            var entry=Path.Combine(directory,GameRuntime.Entry);var root=JsonNode.Parse(File.ReadAllText(entry))!;
            root["plugins"]![0]!["version"]="999";File.WriteAllText(entry,root.ToJsonString());
            Check.Throws(()=>GameRuntime.Load(directory),"version");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
    }
}
