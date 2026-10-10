using System.Text;
using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AuthorProjectTests
{
    public static void Documentation()
    {
        var documents=AuthorDocuments.Installed(AuthorHost.Builtins);
        Check.That(documents.Count==8 && documents.All(d=>d.Markdown.Length>100));
        var root=Path.Combine(Path.GetTempPath(),"bgb-docs-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            foreach(var file in GameAuthorFormats.Template())File.WriteAllBytes(Path.Combine(root,file.Key),file.Value);
            var code=System.Text.RegularExpressions.Regex.Match(documents.Single(d=>d.Topic=="lua").Markdown,"```lua\\s*(.*?)```",System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
            File.WriteAllText(Path.Combine(root,"rules.lua"),code);
            using var game=new PlaySession(GameRuntime.Load(root),1,new Dictionary<string,string>{{"p1","cpu"}});
            Check.That(game.StepCpu() && game.Ended && game.Fault==null);
            foreach(var topic in new[]{"definition-module","definition-state"})
            {
                var json=System.Text.RegularExpressions.Regex.Match(documents.Single(d=>d.Topic==topic).Markdown,"```json\\s*(.*?)```",System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
                var startup=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root,GameRuntime.Entry)))!;
                var loader=startup["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>().StartsWith("definition."))!;
                loader["id"]=topic.Replace('-','.');File.WriteAllText(Path.Combine(root,GameRuntime.Entry),startup.ToJsonString());
                File.WriteAllText(Path.Combine(root,"module.json"),json);_=GameRuntime.Load(root);
            }
        }
        finally {Directory.Delete(root,true);}
    }
    public static void External() => ExternalAsync().GetAwaiter().GetResult();
    private static async Task ExternalAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-external-"+Guid.NewGuid());
        try
        {
            var document=ModuleDocument.Create();document.Save(root);
            using var documents=new DocumentWorkspace();documents.Set(document);
            var identity=new ProjectIdentity(root,"gui","gui",documents.WorkspaceId,documents.Generation);
            using var mode=new WorkspaceEditMode(documents,identity);
            var script=document.Text("rules.lua");documents.EditDraft("rules.lua",script+"\n-- draft");
            File.WriteAllText(Path.Combine(root,"rules.lua"),script+"\n-- external");File.WriteAllText(Path.Combine(root,"new.txt"),"external addition");File.Delete(Path.Combine(root,"cpu.lua"));
            Check.Throws(()=>mode.Read().GetAwaiter().GetResult(),"External files");
            var status=documents.ExternalStatus(identity);Check.That(status.Changes.Length==3 && status.Dirty);
            var reload=System.Text.Json.JsonSerializer.SerializeToElement(new {status.DiskVersion,status.WorkspaceVersion,choice="reload"},AuthorProfiles.Json);
            Check.Throws(()=>documents.ResolveExternal(identity,"reload",reload),"discard drafts");
            File.WriteAllText(Path.Combine(root,"new.txt"),"changed again");
            Check.Throws(()=>documents.ResolveExternal(identity,"stale",reload),"changed while resolving");
            status=documents.ExternalStatus(identity);
            var merged=System.Text.Json.JsonSerializer.SerializeToElement(new {status.DiskVersion,status.WorkspaceVersion,choice="merge",files=new Dictionary<string,string?>
                {{"rules.lua",script+"\n-- draft\n-- external"},{"new.txt","changed again"},{"cpu.lua",document.Text("cpu.lua")}}},AuthorProfiles.Json);
            var receipt=documents.ResolveExternal(identity,"merge",merged);Check.That(receipt.Saved && !documents.Dirty);
            Check.That((await mode.Read()).Text("rules.lua").Contains("-- external") && File.ReadAllText(Path.Combine(root,"rules.lua")).Contains("-- draft"));
            Check.That(documents.ResolveExternal(identity,"merge",merged).ResultVersion==receipt.ResultVersion);
            File.WriteAllText(Path.Combine(root,"new.txt"),"reload clean");status=documents.ExternalStatus(identity);
            documents.ResolveExternal(identity,"refresh",System.Text.Json.JsonSerializer.SerializeToElement(new {status.DiskVersion,status.WorkspaceVersion,choice="reload"},AuthorProfiles.Json));
            Check.That(document.Text("new.txt")=="reload clean");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Connections() => ConnectionsAsync().GetAwaiter().GetResult();
    private static async Task ConnectionsAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-connection-"+Guid.NewGuid());
        try
        {
            var document=ModuleDocument.Create();document.Save(root);
            using var documents=new DocumentWorkspace();documents.Set(document);
            var service=new AuthorWorkspaceService(documents,action=>Task.FromResult(action()));
            for(var iteration=0;iteration<2;iteration++)
            {
                var dropped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var server=new WorkspaceConnectionServer(async (request, token)=>
                {
                    var value=await service.Handle(request,token);
                    if(request.RequestId=="drop-"+iteration)dropped.TrySetResult();
                    return value;
                });server.Publish(documents.WorkspaceId,documents.Generation,root);
                try
                {
                    using var mode=new GuiEditMode(root,"gui",server.Instance);
                    Check.That((await mode.Read()).Source.Identity.Workspace==documents.WorkspaceId);
                    var view=await mode.Read();var changes=view.Change("connected-"+iteration,new Dictionary<string,byte[]?> {{"connected.txt",Encoding.UTF8.GetBytes(iteration.ToString())}});
                    var receipt=await mode.Apply(changes);Check.That(!receipt.Saved && !File.Exists(Path.Combine(root,"connected.txt")));
                    Check.That((await mode.Apply(changes)).ResultVersion==receipt.ResultVersion);
                    Check.That((await mode.Result(changes.RequestId)).State==EditState.Applied);
                    documents.Undo();Check.That(!document.Paths.Contains("connected.txt"));documents.Redo();
                    var beforeDrop=await mode.Read();var drop=beforeDrop.Change("drop-"+iteration,new Dictionary<string,byte[]?> {{"lost.txt",Encoding.UTF8.GetBytes("once")}});
                    var advertisement=WorkspaceConnections.Discover().Single(c=>c.Instance==server.Instance);
                    using(var raw=new System.IO.Pipes.NamedPipeClientStream(".",advertisement.Pipe,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous))
                    {
                        await raw.ConnectAsync(5000);
                        var payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new WorkspaceRequest("apply",drop.Source.Identity,drop.RequestId,drop),AuthorProfiles.Json);
                        await raw.WriteAsync(BitConverter.GetBytes(payload.Length));await raw.WriteAsync(payload);await raw.FlushAsync();
                        // Dispose without reading the server's response.
                    }
                    await dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Check.That((await mode.Result(drop.RequestId)).State==EditState.Applied);
                    Check.That((await mode.Apply(drop)).State==EditState.Applied);
                    var profile=AuthorProfiles.Resolve(root,null) with {Name="gui",Profile=new(1,[new("edit.gui"),new("author.files"),new("author.module"),new("author.validation")],server.Instance)};
                    await using(var host=await AuthorHost.Start(profile))
                    {
                        var validation=(ValidationResult)await host.Invoke("project.validate",System.Text.Json.JsonSerializer.SerializeToElement(new {}),"validate");Check.That(validation.Success && validation.Source.Identity.Mode=="gui");
                        await host.Invoke("workspace.save",System.Text.Json.JsonSerializer.SerializeToElement(new {}),"save-"+iteration);Check.That(File.Exists(Path.Combine(root,"connected.txt")));
                    }
                    await using(var second=new WorkspaceConnectionServer(service.Handle))
                    {
                        second.Publish(documents.WorkspaceId,documents.Generation,root);
                        Check.Throws(()=>new GuiEditMode(root,"gui",null),"Multiple matching");
                    }
                    documents.Set(ModuleDocument.Open(root));server.Publish(documents.WorkspaceId,documents.Generation,root);
                    Check.Throws(()=>mode.Read().GetAwaiter().GetResult(),"generation");
                    document=documents.Current!;
                    // Reset the next cycle through a normal author operation, without touching disk implicitly.
                    File.Delete(Path.Combine(root,"connected.txt"));documents.Set(ModuleDocument.Open(root));document=documents.Current!;
                }
                finally {await server.DisposeAsync();}
                Check.That(!WorkspaceConnections.Discover().Any(c=>c.Instance==server.Instance));
            }
            Check.Throws(()=>new GuiEditMode(root,"gui",null),"No matching");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Workspace() => WorkspaceAsync().GetAwaiter().GetResult();
    private static async Task WorkspaceAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-workspace-"+Guid.NewGuid());
        try
        {
            var document=ModuleDocument.Create();document.Save(root);
            using var workspace=new DocumentWorkspace();workspace.Set(document);
            var identity=new ProjectIdentity(root,"connected","gui",workspace.WorkspaceId,workspace.Generation);
            using var mode=new WorkspaceEditMode(workspace,identity);
            var script=document.Text("rules.lua");workspace.EditDraft("rules.lua",script+"\n-- user draft");
            var view=await mode.Read();Check.That(view.Text("rules.lua").Contains("user draft"));
            var changes=view.Change("gui",new Dictionary<string,byte[]?> {{"rules.lua",Encoding.UTF8.GetBytes(script+"\n-- user draft\n-- agent")},{"note.txt",Encoding.UTF8.GetBytes("new")}});
            var result=await mode.Apply(changes);Check.That(!result.Saved && result.State==EditState.Applied);
            Check.That(File.ReadAllText(Path.Combine(root,"rules.lua"))==script && !File.Exists(Path.Combine(root,"note.txt")));
            Check.That(workspace.Draft("rules.lua").Dirty && document.Text("rules.lua")==script);
            workspace.Undo();Check.That((await mode.Read()).Text("rules.lua")==script+"\n-- user draft" && !document.Paths.Contains("note.txt"));
            workspace.Redo();Check.That((await mode.Read()).Text("rules.lua").Contains("agent") && document.Paths.Contains("note.txt"));
            await mode.Save("save");Check.That(File.ReadAllText(Path.Combine(root,"rules.lua")).Contains("agent"));
            workspace.Undo();Check.That(!document.Paths.Contains("note.txt") && workspace.Draft("rules.lua").Text.Contains("user draft") && !workspace.Draft("rules.lua").Text.Contains("agent"));
            workspace.Redo();Check.That(document.Paths.Contains("note.txt"));
            workspace.Form("fields",()=>"initial");workspace.EditForm("fields","invalid");
            Check.Throws(()=>mode.Read().GetAwaiter().GetResult(),"pending form");workspace.DiscardForms();
            workspace.EditDraft("module.json","{");Check.Throws(()=>mode.Read().GetAwaiter().GetResult(),"JSON");workspace.DiscardDraft("module.json");
            workspace.Set(ModuleDocument.Create());Check.Throws(()=>mode.Apply(changes).GetAwaiter().GetResult(),"generation");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Exports() => ExportsAsync().GetAwaiter().GetResult();
    private static async Task ExportsAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-author-export-"+Guid.NewGuid());
        var output=root+"-outputs";Directory.CreateDirectory(output);
        try
        {
            System.Text.Json.JsonElement Args(object value)=>System.Text.Json.JsonSerializer.SerializeToElement(value);
            foreach(var format in new[]{"module","state"})
            {
                var project=Path.Combine(root,format);
                await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(project,null));
                await host.Invoke("project.create."+format,Args(new {}),"create");
                await host.Invoke("resources.import.image",Args(new {source=Path.GetFullPath("samples/freecell/assets/card_clubs_A.png"),target="assets/card.png",name="card"}),"image");
                var artifact=(AuthorArtifact)await host.Invoke("project.pack",Args(new {target=Path.Combine(output,format+".bgm")}),"pack");
                Check.That(GameRuntime.Load(artifact.Target).Fingerprint==artifact.Fingerprint);
                using(var zip=System.IO.Compression.ZipFile.OpenRead(artifact.Target))Check.That(!zip.Entries.Any(e=>e.FullName.Contains(".bgb-author") || e.FullName==AuthorProfiles.ConfigurationFile));
                Check.Throws(()=>host.Invoke("player.export",Args(new {target=Path.Combine(output,"bad"),player="absent-release"}),"bad").GetAwaiter().GetResult());
                Check.That(!Directory.Exists(Path.Combine(output,"bad")));
                if(format=="module" && Environment.GetEnvironmentVariable("BGB_TEST_RELEASE") is { } release)
                {
                    var game=Path.Combine(output,"standalone");
                    await host.Invoke("player.export",Args(new {target=game,player=Path.GetFullPath(release)}),"export");
                    Check.That(File.Exists(Path.Combine(game,"Play.exe")) && !File.Exists(Path.Combine(game,"Authoring.dll")));
                    var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(game,"Play.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=game};
                    foreach(var value in new[]{"batch",Path.Combine(game,"game.bgm"),"--count","1","--out",Path.Combine(output,"batch")})start.ArgumentList.Add(value);
                    using var process=System.Diagnostics.Process.Start(start)!;await process.WaitForExitAsync();Check.That(process.ExitCode==0);
                }
            }
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);Directory.Delete(output,true);}
    }
    public static void Runs() => RunsAsync().GetAwaiter().GetResult();
    private static async Task RunsAsync()
    {
        System.Text.Json.JsonElement Args(object value)=>System.Text.Json.JsonSerializer.SerializeToElement(value);
        foreach(var sample in new[]{"quick-cards","hex-skirmish"})
        {
            await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(Path.GetFullPath("samples/"+sample),null));
            var result=(RunResult)await host.Invoke("run.cpu",Args(new {seed=11,count=2}),"cpu");
            Check.That(result.Success && result.Runs.All(r=>r.RepeatMatches && r.ReplayMatches));
            var limited=(RunResult)await host.Invoke("run.cpu",Args(new {seed=11,limit=1}),"limit");Check.That(!limited.Success);
        }
        var root=Path.Combine(Path.GetTempPath(),"bgb-debug-"+Guid.NewGuid());var session=root+".session.json";
        try
        {
            await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(root,null));
            await host.Invoke("project.create.module",Args(new {}),"create");
            await host.Invoke("debug.start",Args(new {target=session,seed=12}),"start");
            var original=BoardGame.Kernel.Wire.Read<DebugDocument>(File.ReadAllText(session));
            File.WriteAllText(Path.Combine(root,"rules.lua"),"broken external content");
            await host.Invoke("debug.inspect",Args(new {session}),"inspect");
            Check.Throws(()=>host.Invoke("debug.step",Args(new {session,step=0,action="illegal"}),"illegal").GetAwaiter().GetResult(),"not offered");
            await host.Invoke("debug.step",Args(new {session,step=0,action="finish"}),"finish");
            await host.Invoke("debug.step",Args(new {session,step=0,action="finish"}),"finish");
            var completed=BoardGame.Kernel.Wire.Read<DebugDocument>(File.ReadAllText(session));Check.That(completed.Steps.Length==1 && completed.Source==original.Source);
            await host.Invoke("debug.record",Args(new {session,target=session+".recording"}),"record");
            Check.That(BoardGame.Kernel.Wire.Read<SessionRecording>(File.ReadAllText(session+".recording")).Replay().ExportAuthorityState().Ended);
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
            foreach(var file in new[]{session,session+".lock",session+".recording"})if(File.Exists(file))File.Delete(file);
        }
    }
    public static void Validation() => ValidationAsync().GetAwaiter().GetResult();
    private static async Task ValidationAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-validation-"+Guid.NewGuid());
        try
        {
            await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(root,null));
            var args=System.Text.Json.JsonSerializer.SerializeToElement(new {});
            async Task<ValidationResult> Validate()=>(ValidationResult)await host.Invoke("project.validate",args,Guid.NewGuid().ToString());
            var absent=await Validate();Check.That(!absent.Success && absent.Diagnostics.Single().Path==GameRuntime.Entry);
            await host.Invoke("project.create.module",args,"create");
            var valid=await Validate();Check.That(valid.Success && valid.Checks.Contains("runtime") && valid.CurrentAtCompletion);
            var script=File.ReadAllText(Path.Combine(root,"rules.lua"));
            File.WriteAllText(Path.Combine(root,"rules.lua"),"invalid lua !");var broken=await Validate();Check.That(!broken.Success && broken.Source.Version!=valid.Source.Version);
            File.WriteAllText(Path.Combine(root,"rules.lua"),script);Check.That((await Validate()).Success);
            File.WriteAllText(Path.Combine(root,"module.json"),"{broken");var definition=await Validate();Check.That(!definition.Success && definition.Diagnostics.Single().Line==1);
            Check.That(!Directory.EnumerateDirectories(root).Any(p=>Path.GetFileName(p).StartsWith("bgb-project-")));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Resources() => ResourcesAsync().GetAwaiter().GetResult();
    private static async Task ResourcesAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-author-resources-"+Guid.NewGuid());
        try
        {
            await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(root,null));
            System.Text.Json.JsonElement Args(object value)=>System.Text.Json.JsonSerializer.SerializeToElement(value);
            await host.Invoke("project.create.module",Args(new {}),"create");
            var image=Args(new {source=Path.GetFullPath("samples/freecell/assets/card_clubs_A.png"),target="assets/portrait.png",name="portrait"});
            Check.That(((EditReceipt)await host.Invoke("resources.import.image",image,"image")).State==EditState.Applied);
            Check.That(GameRuntime.Load(root).Resources!.Images.ContainsKey("portrait"));
            await host.Invoke("resources.import.image",image,"image");
            Check.Throws(()=>host.Invoke("resources.import.image",Args(new {source=Path.GetFullPath("samples/freecell/assets/card_clubs_A.png"),target="assets/other.png",name="portrait"}),"collision").GetAwaiter().GetResult(),"conflicting resource");
            Check.That(!File.Exists(Path.Combine(root,"assets/other.png")));
            var atlas=Args(new {source=Path.GetFullPath("samples/poker-uno/assets/cards.json"),target="assets/atlas/cards.json",name="atlas/"});
            Check.That(((EditReceipt)await host.Invoke("resources.import.atlas",atlas,"atlas")).State==EditState.Applied);
            var pool=GameRuntime.Load(root).Resources!;Check.That(pool.Images.Count==54 && pool.Images.ContainsKey("atlas/card_hearts_A"));
            Check.That(((EditReceipt)await host.Invoke("resources.move",Args(new {from="assets/portrait.png",to="images/portrait.png"}),"move")).State==EditState.Applied);
            Check.That(GameRuntime.Load(root).Resources!.Images["portrait"].Resource=="images/portrait.png");
            Check.That(((EditReceipt)await host.Invoke("resources.move",Args(new {from="assets/atlas/cards.json",to="images/atlas.json"}),"move-atlas")).State==EditState.Applied);
            Check.That(GameRuntime.Load(root).Resources!.Images.Count==54);
            var missing=Path.Combine(root,"missing-atlas.json");File.WriteAllText(missing,"{\"frames\":{},\"meta\":{\"image\":\"missing.png\"}}");
            Check.Throws(()=>host.Invoke("resources.import.atlas",Args(new {source=missing,target="bad/atlas.json"}),"bad").GetAwaiter().GetResult(),"missing.png");
            Check.That(!File.Exists(Path.Combine(root,"bad/atlas.json")));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Templates() => TemplatesAsync().GetAwaiter().GetResult();
    private static async Task TemplatesAsync()
    {
        foreach(var format in new[]{"module","state"})
        {
            var root=Path.Combine(Path.GetTempPath(),"bgb-template-"+Guid.NewGuid());
            try
            {
                await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(root,null));
                var args=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"id\":\"test-author\",\"definition\":\"definitions/custom.json\"}");
                var created=(EditReceipt)await host.Invoke("project.create."+format,args,"create");
                Check.That(created.State==EditState.Applied);
                var module=GameRuntime.Load(root);Check.That(module.Id=="test-author");
                using(var game=new PlaySession(module)) Check.That(game.SubmitHuman("p1",game.Actions().Single().Command).Accepted && game.Ended);
                Check.That(((EditReceipt)await host.Invoke("project.create."+format,args,"create")).ResultVersion==created.ResultVersion);
                Check.Throws(()=>host.Invoke("project.create."+format,args,"another").GetAwaiter().GetResult(),"already exists");
                var configuration=await host.Invoke("configuration.read",System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"),"read");
                Check.That(System.Text.Json.JsonSerializer.Serialize(configuration).Contains("definitions/custom.json"));
                var selected=AuthorProfiles.Resolve(root,null) with {Profile=new(1,[new("edit.disk"),new("author.files"),new("author.configuration")])};
                await using var missing=await AuthorHost.Start(selected);
                Check.Throws(()=>missing.Invoke("configuration.read",System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"),"missing").GetAwaiter().GetResult(),"matching author");
                Check.That((await missing.Invoke("files.list",System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"),"generic"))!=null);
            }
            finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
    public static void Crash(string root)
    {
        using var mode=new DiskEditMode(new(root,"builtin","disk")){BeforeWrite=i=>{if(i==1)Environment.Exit(71);}};
        var view=mode.Read().Result;
        mode.Apply(view.Change("process-crash",new Dictionary<string,byte[]?> {{"process-one",[1]},{"process-two",[2]}})).GetAwaiter().GetResult();
    }
    public static void Recovery() => RecoveryAsync().GetAwaiter().GetResult();
    private static async Task RecoveryAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-recovery-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var identity=new ProjectIdentity(root,"builtin","disk");
        try
        {
            using var failing=new DiskEditMode(identity){BeforeWrite=i=>{if(i==1)throw new IOException("injected write failure");}};
            var source=await failing.Read();var changes=source.Change("partial",new Dictionary<string,byte[]?> {{"first.txt",[1]},{"second.txt",[2]}});
            Check.That((await failing.Apply(changes)).State==EditState.PartialFailure && File.Exists(Path.Combine(root,"first.txt")));
            using var mode=new DiskEditMode(identity);
            Check.That((await mode.Result("partial")).AppliedPaths.SequenceEqual(new[]{"first.txt"}));
            File.WriteAllBytes(Path.Combine(root,"first.txt"),[9]);
            Check.That((await mode.Recover("partial")).State==EditState.PartialFailure && File.ReadAllBytes(Path.Combine(root,"first.txt"))[0]==9);
            File.WriteAllBytes(Path.Combine(root,"first.txt"),[1]);
            Check.That((await mode.Recover("partial")).State==EditState.Applied);
            Check.That((await mode.Apply(changes)).State==EditState.Applied);
            Check.Throws(()=>mode.Apply(changes with {Files=[new("other",[3],null)]}).GetAwaiter().GetResult(),"different content");
            Check.That((await mode.Result("expired")).State==EditState.Unknown);
            // An unexpected interruption leaves durable intent but no terminal receipt.
            using var interrupted=new DiskEditMode(identity){BeforeWrite=i=>{if(i==1)throw new InvalidOperationException("simulated interruption");}};
            var next=(await mode.Read()).Change("interrupted",new Dictionary<string,byte[]?> {{"third",[3]},{"fourth",[4]}});
            Check.Throws(()=>interrupted.Apply(next).GetAwaiter().GetResult(),"simulated interruption");
            Check.That((await mode.Result("interrupted")).State==EditState.PartialFailure);
            Check.That((await mode.Recover("interrupted")).State==EditState.Applied);
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(AuthorProjectTests).Assembly.Location);
            start.ArgumentList.Add("--author-crash");start.ArgumentList.Add(root);
            using(var child=System.Diagnostics.Process.Start(start)!) {await child.WaitForExitAsync();Check.That(child.ExitCode==71);}
            Check.That((await mode.Result("process-crash")).AppliedPaths.SequenceEqual(new[]{"process-one"}));
            Check.That((await mode.Recover("process-crash")).State==EditState.Applied);
            var host=await AuthorHost.Start(AuthorProfiles.Resolve(root,null));
            var args=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"files\":{\"idempotent\":\"one\"}}");
            var first=(EditReceipt)await host.Invoke("files.write",args,"stable");
            var again=(EditReceipt)await host.Invoke("files.write",args,"stable");
            Check.That(first.ResultVersion==again.ResultVersion);
            var changed=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"files\":{\"idempotent\":\"two\"}}");
            Check.Throws(()=>host.Invoke("files.write",changed,"stable").GetAwaiter().GetResult(),"different operation");
            await host.DisposeAsync();
        }
        finally {Directory.Delete(root,true);}
    }
    public static void Disk() => DiskAsync().GetAwaiter().GetResult();
    private static async Task DiskAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-disk-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var identity=new ProjectIdentity(root,"builtin","disk");
        try
        {
            File.WriteAllText(Path.Combine(root,"script.lua"),"first");
            using var mode=new DiskEditMode(identity);
            var first=await mode.Read();Check.That(first.Text("script.lua")=="first");
            File.WriteAllText(Path.Combine(root,"script.lua"),"second");
            Check.That((await mode.Read()).Text("script.lua")=="second");
            var stale=first.Change("stale",new Dictionary<string,byte[]?> {{"created.txt",[1]}});
            Check.That((await mode.Apply(stale)).State==EditState.Conflict && !File.Exists(Path.Combine(root,"created.txt")));
            var enumeration=await mode.Read();enumeration.Enumerate("assets/");
            Directory.CreateDirectory(Path.Combine(root,"assets"));File.WriteAllText(Path.Combine(root,"assets/new.txt"),"external");
            Check.That((await mode.Apply(enumeration.Change("enumeration",new Dictionary<string,byte[]?> {{"created.txt",[1]}}))).State==EditState.Conflict);
            var absent=await mode.Read();var add=absent.Change("absent",new Dictionary<string,byte[]?> {{"created.txt",[1]}});
            File.WriteAllText(Path.Combine(root,"created.txt"),"external");Check.That((await mode.Apply(add)).State==EditState.Conflict);
            var fresh=await mode.Read();var edits=fresh.Change("success",new Dictionary<string,byte[]?> {{"script.lua",Encoding.UTF8.GetBytes("third")},{"assets/new.txt",null}});
            Directory.CreateDirectory(Path.Combine(root,".git"));File.WriteAllText(Path.Combine(root,".git/config"),"keep");
            var receipt=await mode.Apply(edits);Check.That(receipt.State==EditState.Applied && receipt.Saved, System.Text.Json.JsonSerializer.Serialize(receipt));
            Check.That(File.ReadAllText(Path.Combine(root,".git/config"))=="keep" && File.ReadAllText(Path.Combine(root,"created.txt"))=="external");
            Check.That(!File.Exists(Path.Combine(root,"assets/new.txt")));
        }
        finally {Directory.Delete(root,true);}
    }
    public static void Operations() => OperationsAsync().GetAwaiter().GetResult();
    private static async Task OperationsAsync()
    {
        var effective=AuthorProfiles.Resolve(Path.GetFullPath("samples/state-minimal"),null);
        var info=new AuthorOperationInfo("test.wait","test.plugin","Lease fixture",new(),new(){["type"]="object"},[],[]);
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var cleaned=false;
        var plugin=new AuthorPlugin("test.plugin","1",1,[],["author.operations"],s=>
        {
            s.Own(new AuthorCleanup(()=>cleaned=true));
            s.Register(new(info,async call=>{entered.SetResult();await release.Task.WaitAsync(call.Cancellation);return new {done=true};}));
        },"Test") {Operations=[info]};
        var catalog=AuthorHost.Builtins.Append(plugin).ToArray();
        Check.Throws(()=>AuthorHost.Validate(effective.Profile,catalog.Append(plugin with {Id="duplicate"}).ToArray()),"operation.duplicate");
        var host=await AuthorHost.Start(effective with {Profile=new(1,[..effective.Profile.Plugins,new("test.plugin")])},catalog);
        var args=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");
        Check.Throws(()=>host.Invoke("files.read",args,"read").GetAwaiter().GetResult(),"path");
        Check.Throws(()=>host.Invoke("missing",args,"read").GetAwaiter().GetResult(),"Enable");
        var operation=host.Invoke("test.wait",args,"lease");await entered.Task;
        var disposal=host.DisposeAsync().AsTask();Check.That(!disposal.IsCompleted && !cleaned);
        release.SetResult();await operation;await disposal;Check.That(cleaned);
        await using var disabled=await AuthorHost.Start(effective,catalog);
        Check.That(disabled.Installed.Any(p=>p.Id=="test.plugin") && !disabled.Enabled.Any(p=>p.Id=="test.plugin"));
        Check.Throws(()=>disabled.Invoke("test.wait",args,"disabled").GetAwaiter().GetResult(),"Enable");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Check.Throws(()=>disabled.Invoke("files.list",args,"cancel",cancellation.Token).GetAwaiter().GetResult(),"canceled");
    }
    public static void Profiles()
    {
        var root=Path.Combine(Path.GetTempPath(),"bgb-profiles-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            Check.That(AuthorProfiles.Resolve(root,null).Source=="builtin");
            Check.Throws(()=>AuthorProfiles.Resolve(root,"missing"),"profile.missing");
            File.WriteAllText(Path.Combine(root,AuthorProfiles.ConfigurationFile),"""
                {"defaultProfile":"local","profiles":{"local":{"format":1,"plugins":[{"id":"edit.disk","version":"1","api":1}]},"none":{"format":1,"plugins":[]}}}
                """);
            Check.That(AuthorProfiles.Resolve(root,null).Source=="project-default");
            var selected=AuthorProfiles.Resolve(root,"local");Check.That(selected.Source=="explicit");
            Check.Throws(()=>AuthorHost.Validate(AuthorProfiles.Resolve(root,"none").Profile,AuthorHost.Builtins),"profile.mode");
            foreach(var profile in new[]{new AuthorProfile(1,[new("missing")]),new(1,[new("edit.disk","2")]),new(1,[new("edit.disk"),new("edit.disk")])})
                Check.Throws(()=>AuthorHost.Validate(profile,AuthorHost.Builtins),"plugin.");
            var count=0;
            var bad=new AuthorPlugin("broken","1",1,[],["author.formats"],s=>{s.Own(new Cleanup(()=>count--));count++;throw new IOException("injected");},"Failure fixture");
            var broken=selected with {Profile=new(1,[new("edit.disk"),new("broken")])};
            Check.Throws(()=>AuthorHost.Start(broken,AuthorHost.Builtins.Append(bad).ToArray()).GetAwaiter().GetResult(),"plugin.initialize");
            Check.That(count==0,"Failed plugin leaked its effect");
            for(var i=0;i<3;i++)
            {
                var host=AuthorHost.Start(selected).GetAwaiter().GetResult();
                Check.That(host.Enabled.Count==2 && host.Service<IProjectEditMode>("author.mode").Read().Result.Source.Identity.Profile=="local");
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            File.WriteAllText(Path.Combine(root,"game.start.json"),"invalid");Directory.CreateDirectory(Path.Combine(root,"child"));
            Check.That(AuthorProfiles.Locate(null,Path.Combine(root,"child"))==root);
        }
        finally {Directory.Delete(root,true);}
    }
    public static void Contract()
    {
        var identity=new ProjectIdentity(Path.GetFullPath("."),"default","disk");
        var bytes=Encoding.UTF8.GetBytes("broken script");
        var view=new ProjectView(identity,new Dictionary<string,byte[]> {{"rules.lua",bytes}});
        bytes[0]=0;
        Check.That(view.Text("rules.lua")=="broken script");
        Check.That(!view.Exists(GameRuntime.Entry));
        view.Enumerate("assets/");
        var changes=view.Change("repair",new Dictionary<string,byte[]?> {{GameRuntime.Entry,Encoding.UTF8.GetBytes("{}")}});
        Check.That(view.Conflicts(changes).Length==0);
        var concurrent=view.CopyFiles();concurrent["assets/new.png"]=[1];
        Check.That(new ProjectView(identity,concurrent).Conflicts(changes).Any(d=>d.Code=="project.enumeration"));
        concurrent["rules.lua"]=[2];
        Check.That(new ProjectView(identity,concurrent).Conflicts(changes).Any(d=>d.Path=="rules.lua"));
        Check.That(new ProjectView(identity with {Generation=1},view.CopyFiles()).Conflicts(changes).Any(d=>d.Code=="project.identity"));
        foreach(var path in new[]{"../escape","C:/escape","a\\b","a/../b",".git/config",".bgb-author/journal","a:stream","AUX.txt"})
            Check.Throws(()=>view.Exists(path),"path");
        var formats=new GameAuthorFormats();
        var module=new ProjectView(identity,GameAuthorFormats.Template());
        var format=formats.Resolve(module)!;
        Check.That(format.DefinitionPath=="module.json" && format.Read(module).ContainsKey("entities"));
        using(formats.Register(GameAuthorFormats.StateProvider))
        {
            var state=new ProjectView(identity,ProjectPaths.Read("samples/state-minimal"));
            var stateFormat=formats.Resolve(state)!;
            var config=stateFormat.Read(state);config["phases"]!.AsArray().Add("author-test");
            Check.That(stateFormat.Serialize(state,config).Contains("author-test"));
        }
        var root=Path.Combine(Path.GetTempPath(),"bgb-incomplete-"+Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"rules.lua"),"broken script");
            var document=ModuleDocument.Open(root);
            Check.That(document.Text("rules.lua")=="broken script");
            document.EditText(GameRuntime.Entry,"{}");
            Check.Throws(()=>document.Snapshot());
            document.Undo();Check.That(!document.Paths.Contains(GameRuntime.Entry));
        }
        finally {Directory.Delete(root,true);}
    }
}
