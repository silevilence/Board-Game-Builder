using System.Text;
using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AuthorProjectTests
{
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
