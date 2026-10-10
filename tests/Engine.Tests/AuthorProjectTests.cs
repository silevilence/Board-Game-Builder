using System.Text;
using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AuthorProjectTests
{
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
