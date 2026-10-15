using System.Text.Json;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowDeliveryTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-flow-delivery-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var flowOnly=Path.Combine(root,"flow-only");Directory.CreateDirectory(flowOnly);
            var graph=FlowTests.Minimal();graph.Nodes[1]=graph.Nodes[1] with {Script=new("execute.lua")};
            FlowTests.WriteGame(flowOnly,graph,new(){{"execute.lua","return {execute=function(x) return {} end}"}});
            var entryPath=Path.Combine(flowOnly,GameRuntime.Entry);var entry=File.ReadAllText(entryPath);
            var startup=Wire.Read<RuntimeStartup>(entry);
            startup.Plugins.Add(new("rules.commands","1",1,JsonSerializer.SerializeToElement(new{})));
            File.WriteAllText(entryPath,Wire.Json(startup));Check.Throws(()=>GameRuntime.Load(flowOnly),"capability");
            startup=Wire.Read<RuntimeStartup>(entry);var index=startup.Plugins.FindIndex(p=>p.Id=="flow");
            startup.Plugins[index]=startup.Plugins[index] with {Version="unknown"};
            File.WriteAllText(entryPath,Wire.Json(startup));Check.Throws(()=>GameRuntime.Load(flowOnly),"version");
            File.WriteAllText(entryPath,entry);
            var release=Path.Combine(root,"release");Directory.CreateDirectory(release);
            var files=new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach(var path in new[]{"Player.exe","Player.dll","Player.deps.json","Player.runtimeconfig.json","Runtime.dll","Modules.dll","Kernel.dll","Kernel.Abstractions.dll","coreclr.dll","hostfxr.dll","CordiSharp.dll"})files[path]="stub";
            File.WriteAllText(Path.Combine(release,"release.json"),Wire.Json(new ReleaseManifest(EngineVersion.Api,"1","win-x64",files,true,false,GameRuntime.PluginVersions)));
            Check.Throws(()=>PlayerDistribution.Preflight(release,flowOnly),"worker/Lua.exe");
            using(var minimal=new PlaySession(GameRuntime.Load("samples/flow-minimal"))) Check.That(minimal.Ended);
            var source=Path.Combine(root,"source");TestModules.CopyGame("samples/flow-cards",source);
            var module=GameRuntime.Load(source);
            var sawWin=false;
            for(uint seed=1;seed<=12;seed++)
            {
                using var game=new PlaySession(module,seed,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"),30);
                while(game.StepCpu()) { }
                Check.That(game.Ended && game.Fault==null && game.Steps==6);
                Check.That(game.AuthorityStateHash()==Wire.Hash(Wire.Json(game.ExportRecording().Replay().ExportAuthorityState())));
                sawWin|=game.ExportAuthorityState().Winners.Count==1;
            }
            Check.That(sawWin);
            var definitionPath=Path.Combine(source,"module.json");var originalDefinition=File.ReadAllText(definitionPath);
            var tied=System.Text.Json.Nodes.JsonNode.Parse(originalDefinition)!;
            foreach(var entity in tied["entities"]!.AsArray())
                foreach(var component in entity!["components"]!.AsArray())
                    if(component!["type"]!.GetValue<string>()=="properties" && component["data"]!["value"]!=null) component["data"]!["value"]=1;
            File.WriteAllText(definitionPath,tied.ToJsonString());
            using(var tie=new PlaySession(GameRuntime.Load(source),controllers:module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu")))
            {
                while(tie.StepCpu()) { }
                Check.That(tie.Ended && tie.ExportAuthorityState().Flow!.Node=="draw" && tie.ExportAuthorityState().Winners.Count==0);
            }
            File.WriteAllText(definitionPath,originalDefinition);
            using(var mixed=new PlaySession(module))
            {
                Check.That(mixed.ExportAuthorityState().Entities["hand-p1"].Contents.Count==3,"Post-deal condition failed");
                while(!mixed.Ended && mixed.Fault==null)
                    if(mixed.IsCpu) mixed.StepCpu();else Check.That(mixed.SubmitHuman(mixed.CurrentSeat,mixed.Actions()[0].Command).Accepted);
                Check.That(mixed.Ended && mixed.Fault==null);
            }
            await using(var host=await AuthorHost.Start(AuthorProfiles.Resolve(source,null)))
            {
                var sessionFile=Path.Combine(root,"debug.json");
                await host.Invoke("debug.start",JsonSerializer.SerializeToElement(new{target=sessionFile}),"debug-start");
                var debug=Wire.Read<DebugDocument>(File.ReadAllText(sessionFile));Check.That(debug.Recording!=null && debug.Recording.Log.EntryCount==3);
                await host.Invoke("debug.inspect",JsonSerializer.SerializeToElement(new{session=sessionFile}),"debug-inspect");
                Check.That(File.ReadAllText(sessionFile)==Wire.Json(debug));
                using(var playable=PlaySession.Restore(debug.Module,debug.Recording!))
                    await host.Invoke("debug.step",JsonSerializer.SerializeToElement(new{session=sessionFile,step=0,action=playable.Actions()[0].Id}),"debug-step");
                var stepped=Wire.Read<DebugDocument>(File.ReadAllText(sessionFile));Check.That(stepped.Recording!.Log.EntryCount>debug.Recording!.Log.EntryCount);
                var model=(FlowAuthorModel)await host.Invoke("flow.read",JsonSerializer.SerializeToElement(new{}),"read");
                Check.That(model.Success && model.Definition.Nodes.Count==8);
                var replacement=model.Definition with {Edges=[..model.Definition.Edges.Where(e=>e.Id!="dealt")]};
                var changed=(EditReceipt)await host.Invoke("flow.replace",JsonSerializer.SerializeToElement(new{version=model.Source.Version,definition=replacement},Wire.Options),"edit");
                Check.That(changed.State==EditState.Applied);
                var invalid=(FlowAuthorModel)await host.Invoke("flow.validate",JsonSerializer.SerializeToElement(new{}),"check");
                Check.That(!invalid.Success && invalid.Diagnostics.Any(d=>d.Code=="no-terminal-path"));
                var conflict=false;try {await host.Invoke("flow.replace",JsonSerializer.SerializeToElement(new{version=model.Source.Version,definition=model.Definition},Wire.Options),"stale");}
                catch(AuthorOperationException e) {conflict=e.Code=="project.conflict";}
                Check.That(conflict);
                await host.Invoke("flow.replace",JsonSerializer.SerializeToElement(new{version=invalid.Source.Version,definition=model.Definition},Wire.Options),"restore");
            }
            module=GameRuntime.Load(source);
            var package=Path.Combine(root,"game.bgm");GamePackage.Pack(source,package);Directory.Delete(source,true);
            var packed=GameRuntime.Load(package);
            Check.That(module.Fingerprint==packed.Fingerprint && packed.FlowFiles!.Count==5);
            var snapshot=Path.Combine(root,"snapshot.json");File.WriteAllText(snapshot,Wire.Json(packed));
            using var session=new PlaySession(GameRuntime.Load(snapshot),controllers:packed.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
            while(session.StepCpu()) { }
            Check.That(session.Ended && session.Fault==null);
            var tampered=Wire.Copy(packed);tampered.FlowFiles!["turn.lua"][0]^=1;
            Check.Throws(()=>GameRuntime.ValidateSnapshot(tampered));
        }
        finally {Directory.Delete(root,true);}
    }
}
