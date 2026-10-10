using System.Text;
using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AuthorProjectTests
{
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
            var receipt=await mode.Apply(edits);Check.That(receipt.State==EditState.Applied && receipt.Saved);
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
