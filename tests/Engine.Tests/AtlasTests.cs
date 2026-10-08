using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AtlasTests
{
    public static void Run()
    {
        var module=GameRuntime.Load("samples/poker-uno");var catalog=new ImageCatalog(module.ResourceData!);
        var heart=catalog.Resolve("card_hearts_A");Check.That(heart.Resource=="cards/image" && heart.X==2 && heart.Y==390);
        Check.That(module.Initial.Entities.Values.SelectMany(e=>e.FaceResources).All(r=>!r.Contains('#')));
        Check.That(module.RuntimeIdentity!.ContentHashes.ContainsKey("assets/cards.png") && module.RuntimeIdentity.ContentHashes.ContainsKey("assets/cards.json"));
        var original=JsonNode.Parse(File.ReadAllText("samples/poker-uno/assets/cards.json"))!.AsObject();
        SortedDictionary<string,byte[]> Data(JsonObject atlas)=>new(module.ResourceData!,StringComparer.Ordinal) { ["cards"]=Encoding.UTF8.GetBytes(atlas.ToJsonString()) };
        var trimmed=original.DeepClone().AsObject();
        trimmed["frames"]!["card_hearts_A"]=JsonNode.Parse("""
            {"frame":{"x":2,"y":2,"w":10,"h":20},"rotated":true,"trimmed":true,
             "spriteSourceSize":{"x":3,"y":4,"w":20,"h":10},"sourceSize":{"w":30,"h":40}}
            """);
        var rotated=new ImageCatalog(Data(trimmed)).Resolve("card_hearts_A");
        Check.That(rotated.Rotated && rotated.Destination(10,20,300,400)==(40f,60f,200f,100f));
        void Invalid(Action<JsonObject> change,string message)
        {var copy=original.DeepClone().AsObject();change(copy);Check.Throws(()=>new ImageCatalog(Data(copy)),message);}
        Invalid(a=>a["frames"]=new JsonArray(),"JSON Hash");
        Invalid(a=>a["frames"]!["card_hearts_A"]!["frame"]!["x"]=2000,"outside resource");
        Invalid(a=>a["meta"]!["size"]!["w"]=1800,"meta.size");
        Invalid(a=>a["frames"]!["card_hearts_A"]!["sourceSize"]!["w"]=1,"source/trim");
        Invalid(a=>a["meta"]!["related_multi_packs"]=new JsonArray("other.json"),"multi-pack");
        var collision=Data(original);collision["card_hearts_A"]=[1];Check.Throws(()=>new ImageCatalog(collision),"conflicting frame name");
        var second=Data(original);second["other"]=second["cards"];second["other/image"]=second["cards/image"];
        Check.Throws(()=>new ImageCatalog(second),"conflicting frame name");
        var duplicate="{\"frames\":{},\"frames\":{},\"meta\":{\"image\":\"a.png\"}}";
        Check.Throws(()=>ImageCatalog.ReadAtlas(Encoding.UTF8.GetBytes(duplicate),"duplicate.json"),"duplicate key");
        var missing=Data(original);missing.Remove("cards/image");Check.Throws(()=>new ImageCatalog(missing),"unknown resource");
        Check.Throws(()=>catalog.Resolve("missing-frame").Validate(module.ResourceData!),"unknown resource");
        var doc=ModuleDocument.Open("samples/poker-uno");
        var bad=original.DeepClone().AsObject();bad["meta"]!["image"]="../../outside.png";doc.EditText("assets/cards.json",bad.ToJsonString());
        Check.Throws(()=>doc.Snapshot(),"path");
        doc.Undo();bad["meta"]!["image"]="missing.png";doc.EditText("assets/cards.json",bad.ToJsonString());Check.Throws(()=>doc.Snapshot(),"missing file");
        doc.Undo();doc.EditText("assets/cards.json",trimmed.ToJsonString());
        var updated=doc.Snapshot();Check.That(updated.Fingerprint!=module.Fingerprint);
        GameRuntime.ValidateSnapshot(updated);
        // Names and metadata survive serialization, with no per-frame image expansion.
        var snapshot=Wire.Read<LoadedModule>(Wire.Json(updated));GameRuntime.ValidateSnapshot(snapshot);
        Check.That(new ImageCatalog(snapshot.ResourceData!).Resolve("card_hearts_A").Rotated);
    }
}
