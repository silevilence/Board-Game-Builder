using System.Text.Json.Nodes;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class AtlasTests
{
    public static void Run()
    {
        var module=GameRuntime.Load("samples/poker-uno");var pool=module.Resources!;
        var heart=pool.GetImage("card_hearts_A");Check.That(heart.Resource=="assets/cards.png" && heart.X==2 && heart.Y==390);
        Check.That(pool.Images.Count==53 && pool.Files.Keys.Count(p=>p.EndsWith(".png"))==1);
        var whole=ModuleDocument.Open("samples/poker-uno");
        var startup=JsonNode.Parse(whole.Text(GameRuntime.Entry))!;
        var config=startup["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="resources.texturepacker")!["configuration"]!;
        config["sources"]![0]!.AsObject().Remove("pattern");
        whole.EditText(GameRuntime.Entry,startup.ToJsonString());var wholeModule=whole.Snapshot();
        Check.That(wholeModule.Resources!.Images.Count==53,"Whole atlas directory tried to parse its PNG/license");
        GameRuntime.ValidateSnapshot(Wire.Read<LoadedModule>(Wire.Json(wholeModule)));
        var original=JsonNode.Parse(File.ReadAllText("samples/poker-uno/assets/cards.json"))!.AsObject();
        LoadedModule Load(JsonObject atlas)
        {var doc=ModuleDocument.Open("samples/poker-uno");doc.EditText("assets/cards.json",atlas.ToJsonString());return doc.Snapshot();}
        var trimmed=original.DeepClone().AsObject();trimmed["frames"]!["card_hearts_A"]=JsonNode.Parse("""
            {"frame":{"x":2,"y":2,"w":10,"h":20},"rotated":true,"trimmed":true,
             "spriteSourceSize":{"x":3,"y":4,"w":20,"h":10},"sourceSize":{"w":30,"h":40}}
            """);
        var updated=Load(trimmed);var rotated=updated.Resources!.GetImage("card_hearts_A");
        Check.That(rotated.Rotated && rotated.Destination(10,20,300,400)==(40f,60f,200f,100f));
        void Invalid(Action<JsonObject> change,string message)
        {var copy=original.DeepClone().AsObject();change(copy);Check.Throws(()=>Load(copy),message);}
        Invalid(a=>a["frames"]=new JsonArray(),"JSON Hash");
        Invalid(a=>a["frames"]!["card_hearts_A"]!["frame"]!["x"]=2000,"outside resource");
        Invalid(a=>a["meta"]!["size"]!["w"]=1800,"meta.size");
        Invalid(a=>a["frames"]!["card_hearts_A"]!["sourceSize"]!["w"]=1,"source/trim");
        Invalid(a=>a["meta"]!["related_multi_packs"]=new JsonArray("other.json"),"multi-pack");
        Invalid(a=>a["meta"]!["image"]="../../outside.png","path");
        Invalid(a=>a["meta"]!["image"]="missing.png","missing file");
        var duplicate=ModuleDocument.Open("samples/poker-uno");duplicate.EditText("assets/cards.json","{\"frames\":{},\"frames\":{},\"meta\":{\"image\":\"a.png\"}}");
        Check.Throws(()=>duplicate.Snapshot(),"duplicate key");
        Check.Throws(()=>pool.GetImage("missing-frame"),"unknown image resource");
        var collision=ModuleDocument.Open("samples/poker-uno");collision.EditText("assets/other.json",original.ToJsonString());
        Check.Throws(()=>collision.Snapshot(),"conflicting resource name");
        Check.That(updated.Fingerprint!=module.Fingerprint);
        var snapshot=Wire.Read<LoadedModule>(Wire.Json(updated));GameRuntime.ValidateSnapshot(snapshot);
        Check.That(snapshot.Resources!.GetImage("card_hearts_A").Rotated);
        var altered=new ResourcePool(snapshot.Resources.Files,snapshot.Resources.Images.ToDictionary(p=>p.Key,p=>p.Key=="card_hearts_A"?p.Value with {X=99}:p.Value),snapshot.Resources.Binaries);
        var tampered=snapshot with {Resources=altered,Definition=snapshot.Definition! with {Resources=altered}};
        tampered=tampered with {Fingerprint=BoardGame.Modules.ModuleSnapshot.Fingerprint(tampered)};
        Check.Throws(()=>GameRuntime.ValidateSnapshot(tampered),"pool snapshot/configuration");
    }
}
