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
            TestModules.CopyGame("samples/quick-cards",directory);
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
            void Reject(LoadedModule candidate,string message)
            {
                candidate=candidate with {Fingerprint=ModuleSnapshot.Fingerprint(candidate)};
                Check.Throws(()=>GameRuntime.ValidateSnapshot(candidate),message);
            }
            var unsupported=Wire.Copy(changed);unsupported.RuntimeIdentity!.Startup.Plugins[0]=unsupported.RuntimeIdentity.Startup.Plugins[0] with {Version="999"};
            Reject(unsupported,"version");
            var resource=Wire.Copy(changed);resource.ResourceHashes!["probe"]="bad";resource.ResourceData!["probe"]=[1];
            Reject(resource,"Legacy resource snapshot");
            var pooled=GameRuntime.Load("samples/freecell");
            var files=pooled.Resources!.Files.ToDictionary(p=>p.Key,p=>p.Value);files[files.Keys.First()]=new byte[]{1};
            var altered=new ResourcePool(files,pooled.Resources.Images,pooled.Resources.Binaries);
            Reject(pooled with {Resources=altered,Definition=pooled.Definition! with {Resources=altered}},"resource content identity");
            var wrongPath=changed with {RulesPath="different.lua"};Reject(wrongPath,"snapshot conflict");
            var wasm=GameRuntime.Load("samples/wasm-counter");
            var selection=wasm.RuntimeIdentity!.Startup.Plugins.Single(p=>p.Id=="wasm");
            var config=JsonNode.Parse(selection.Configuration.GetRawText())!;config["extensions"]![0]!["version"]="2";
            wasm.RuntimeIdentity.Startup.Plugins[wasm.RuntimeIdentity.Startup.Plugins.IndexOf(selection)]=selection with {Configuration=System.Text.Json.JsonSerializer.SerializeToElement(config)};
            Reject(wasm,"WASM configuration/content");
            var entry=Path.Combine(directory,GameRuntime.Entry);var root=JsonNode.Parse(File.ReadAllText(entry))!;
            root["plugins"]![0]!["version"]="999";File.WriteAllText(entry,root.ToJsonString());
            Check.Throws(()=>GameRuntime.Load(directory),"version");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
    }
}
