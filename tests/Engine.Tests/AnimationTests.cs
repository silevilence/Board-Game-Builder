using System.Text.Json.Nodes;
using BoardGame.Authoring;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace Engine.Tests;

public static class AnimationTests
{
    public static AnimationLibrary Definition=>new(1,[new("deal",new("cue","sequence"),[new("card",[new(100,0,0,-10),new(300,20,-30,5),new(600)],"deck","back")])]);
    private static string Fixture()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-animation-"+Guid.NewGuid());TestModules.CopyGame("samples/state-minimal",folder);
        var initial=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"position.json")))!;
        initial["state"]!["entities"]=JsonNode.Parse("""{"cue":{"id":"cue","properties":{"sequence":1,"viewHidden":1}},"deck":{"id":"deck","x":20,"y":30,"properties":{"viewStyle":1}},"card":{"id":"card","x":300,"y":250,"faces":["card"],"faceResources":["back"],"properties":{"viewStyle":1,"viewWidth":80,"viewHeight":120}}}""");
        File.WriteAllText(Path.Combine(folder,"position.json"),initial.ToJsonString());
        File.Copy("samples/freecell/assets/card_clubs_A.png",Path.Combine(folder,"back.png"));
        var startup=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,GameRuntime.Entry)))!;
        startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="resources",["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
        startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="resources.images",["version"]="1",["api"]=1,["configuration"]=JsonNode.Parse("""{"sources":[{"path":"back.png","name":"back"}]}""")});
        startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="resources.animations",["version"]="1",["api"]=1,["configuration"]=new JsonObject{["path"]="animations.json"}});
        File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),startup.ToJsonString());File.WriteAllText(Path.Combine(folder,"animations.json"),AnimationFile.Write(Definition));return folder;
    }
    public static void Runtime()
    {
        var folder=Fixture();
        try
        {
            var module=GameRuntime.Load(folder);GameRuntime.ValidateSnapshot(Wire.Copy(module));
            var scene=TableScene.From(new GameEngine(module.Initial).Project("p1"),module.Resources);
            var player=new SceneAnimationPlayer();player.Show(scene,1000);Check.That(player.Busy);
            var target=scene.Items.Single(i=>i.Entity=="card");var source=scene.Items.Single(i=>i.Entity=="deck");
            Check.That(player.Frame(1000).Items.Single(i=>i.Entity=="card").Bounds.X==source.Bounds.X);
            player.Show(scene,1250);Check.That(player.Frame(1300).Items.Single(i=>i.Entity=="card").Bounds!=target.Bounds,"No intermediate animation");
            Check.That(player.Frame(1600).Items.Single(i=>i.Entity=="card")==target && !player.Busy,"Final pose differs from authority");
            player.Show(scene,1700);Check.That(!player.Busy,"Refresh replayed cue");
            player.Show(scene with {AnimationCues=new Dictionary<string,int>{{"deal",2}}},1800);Check.That(player.Busy);
            player.Show(scene with {AnimationCues=new Dictionary<string,int>{{"deal",0}}},1820);Check.That(!player.Busy,"Reset cue retained old playback");
            player.Show(scene with {AnimationCues=new Dictionary<string,int>{{"deal",2}}},1830);Check.That(player.Busy);
            player.Reset();Check.That(!player.Busy && player.Frame(1850).Items.SequenceEqual(scene.Items));
            var hidden=scene with {Items=scene.Items.Where(i=>i.Entity!="card").ToArray()};player.Show(hidden,2000);
            Check.That(player.Frame(2200).Items.All(i=>i.Entity!="card"),"Animation revealed hidden object");
            var package=Path.Combine(folder,"game.bgm");GamePackage.Pack(folder,package);Check.That(GameRuntime.Load(package).Fingerprint==module.Fingerprint);
            var broken=Definition with {Clips=[Definition.Clips[0] with {Tracks=[Definition.Clips[0].Tracks[0] with {Entity="missing"}]}]};
            File.WriteAllText(Path.Combine(folder,"animations.json"),AnimationFile.Write(broken));Check.Throws(()=>GameRuntime.Load(folder),"missing entity");
            File.WriteAllText(Path.Combine(folder,"animations.json"),AnimationFile.Write(Definition with {Clips=[Definition.Clips[0] with {Trigger=new("cue","typo")}]}));Check.Throws(()=>GameRuntime.Load(folder),"missing trigger property");
            Check.Throws(()=>AnimationFile.Parse("""{"format":1,"clips":[],"unknown":1}"""),"unknown");
            Check.Throws(()=>AnimationFile.Parse(Wire.Json(Definition with {Clips=[Definition.Clips[0] with {Tracks=[new("card",[new(0),new(10,1)])]}]})),"authoritative pose");
            Check.Throws(()=>AnimationFile.Parse(Wire.Json(Definition with {Clips=[Definition.Clips[0],Definition.Clips[0]]})),"duplicate");
        }
        finally {Directory.Delete(folder,true);}
    }
    public static void Author()
    {
        var folder=Fixture();
        try
        {
            using var documents=new DocumentWorkspace();documents.Set(ModuleDocument.Open(folder));var editor=new AnimationWorkspace(documents);
            var original=editor.Draft().Text;documents.EditForm(AnimationWorkspace.FormKey,"invalid json");Check.Throws(editor.Apply,"animations");
            Check.That(editor.Draft().Text=="invalid json" && documents.HasPendingForms);
            documents.EditForm(AnimationWorkspace.FormKey,AnimationFile.Write(Definition with {Clips=[]}));editor.Apply();
            Check.That(editor.Read().Definition.Clips.Count==0 && !documents.HasPendingForms);Check.That(File.ReadAllText(Path.Combine(folder,"animations.json"))==original,"Edit implicitly saved");
            documents.Undo();Check.That(editor.Read().Definition.Clips.Count==1);documents.Redo();Check.That(editor.Read().Definition.Clips.Count==0);
            documents.Save();documents.Set(ModuleDocument.Open(folder));Check.That(editor.Read().Definition.Clips.Count==0);
            _=editor.Draft();documents.EditForm(AnimationWorkspace.FormKey,AnimationFile.Write(Definition));
            documents.EditDraft("position.json",documents.Draft("position.json").Text+"\n");Check.Throws(editor.Apply,"source changed");Check.That(documents.HasPendingForms);
            documents.DiscardForms();documents.DiscardDraft("position.json");
            using var mode=new DiskEditMode(new(folder,"test","disk"));var view=mode.Read().GetAwaiter().GetResult();
            var change=AnimationOperations.Change(view,Definition,"animation-operation");
            var result=mode.Apply(change).GetAwaiter().GetResult();Check.That(result.State==EditState.Applied);
        }
        finally {Directory.Delete(folder,true);}
    }
}
