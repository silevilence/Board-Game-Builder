using System.Text.Json.Nodes;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ResourcePluginTests
{
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-resource-plugins-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/state-minimal",folder);Directory.CreateDirectory(Path.Combine(folder,"assets/sub"));
            var image=File.ReadAllBytes("samples/freecell/assets/card_clubs_A.png");
            File.WriteAllBytes(Path.Combine(folder,"assets/a.png"),image);File.WriteAllBytes(Path.Combine(folder,"assets/sub/b.png"),image);
            var entry=Path.Combine(folder,GameRuntime.Entry);var initial=JsonNode.Parse(File.ReadAllText(entry))!.AsObject();
            LoadedModule Load(string sources,bool images=true,string? raw=null)
            {
                var startup=initial.DeepClone().AsObject();var plugins=startup["plugins"]!.AsArray();
                void Add(string id,string config)=>plugins.Add(new JsonObject {["id"]=id,["version"]="1",["api"]=1,["configuration"]=JsonNode.Parse(config)});
                Add("resources",raw ?? "{}");if(images)Add("resources.images","{\"sources\":"+sources+"}");
                File.WriteAllText(entry,startup.ToJsonString());return GameRuntime.Load(folder);
            }
            var single=Load("""[{"path":"assets/a.png","name":"portrait"}]""");
            Check.That(single.Resources!.Images.Keys.SequenceEqual(new[]{"portrait"}));
            var direct=Load("""[{"path":"assets","pattern":"*.png"}]""");Check.That(direct.Resources!.Images.Keys.SequenceEqual(new[]{"a"}));
            var recursive=Load("""[{"path":"assets","pattern":"**/*.png","prefix":"ui/"}]""");
            Check.That(recursive.Resources!.Images.Keys.SequenceEqual(new[]{"ui/a","ui/sub/b"}));
            var entire=Load("""[{"path":"assets"}]""");Check.That(entire.Resources!.Images.Count==2);
            var question=Load("""[{"path":"assets/sub","pattern":"?.png"}]""");Check.That(question.Resources!.Images.Keys.Single()=="b");
            File.WriteAllText(Path.Combine(folder,"assets/License.txt"),"test asset license");
            Check.That(Load("""[{"path":"assets"}]""").Resources!.Images.Count==2,"Whole image directory tried to decode supporting files");
            Check.Throws(()=>Load("""[{"path":"assets","pattern":"**/*"}]"""),"image");
            Check.Throws(()=>Load("""[{"path":"../outside"}]"""),"path");
            Check.Throws(()=>Load("""[{"path":"assets","pattern":"../*.png"}]"""),"path");
            Check.Throws(()=>Load("""[{"path":"assets","pattern":"*.jpg"}]"""),"matched no files");
            Check.Throws(()=>Load("""[{"path":"assets","name":"one"}]"""),"single file");
            Check.Throws(()=>Load("""[{"path":"assets/a.png","pattern":"*.png"}]"""),"requires a directory");
            Check.Throws(()=>Load("""[{"path":"assets/a.png"},{"path":"assets","pattern":"*.png"}]"""),"conflicting resource name");
            // Opaque files are not guessed to be image/atlas formats.
            File.WriteAllText(Path.Combine(folder,"opaque.json"),"{\"frames\":\"not an atlas\"}");
            var raw=Load("[]",false,"""{"sources":[{"path":"opaque.json","name":"metadata"}]}""");
            Check.That(raw.Resources!.Images.Count==0 && raw.Resources.GetBinary("metadata").Length>0);
            var both=Load("""[{"path":"assets"}]""",true,"""{"sources":[{"path":"opaque.json"}]}""");
            var startup=JsonNode.Parse(File.ReadAllText(entry))!;startup["plugins"]!.AsArray().Remove(startup["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="resources"));File.WriteAllText(entry,startup.ToJsonString());
            Check.Throws(()=>GameRuntime.Load(folder),"missing capability resources");
            _=Load("""[{"path":"assets"}]""",true,"""{"sources":[{"path":"opaque.json"}]}""");
            var package=Path.Combine(folder,"game.bgm");GamePackage.Pack(folder,package);Check.That(GameRuntime.Load(package).Fingerprint==both.Fingerprint);
            var serialized=Wire.Json(both);
            File.Delete(Path.Combine(folder,"assets/a.png"));File.Delete(Path.Combine(folder,"assets/sub/b.png"));
            var snapshot=Wire.Read<LoadedModule>(serialized);GameRuntime.ValidateSnapshot(snapshot);
            Check.That(snapshot.Resources!.Images.Count==2 && snapshot.Resources.GetFile("assets/a.png").Span.SequenceEqual(image));
            var mutable=new Dictionary<string,ReadOnlyMemory<byte>> {{"a",image}};
            var frozen=new ResourcePool(mutable,new Dictionary<string,ImageReference>(),new Dictionary<string,string>());mutable.Clear();image[0]=0;
            Check.That(frozen.GetFile("a").Span[0]==137,"Pool retained caller-owned mutable bytes");
            Check.Throws(()=>((IDictionary<string,ReadOnlyMemory<byte>>)frozen.Files).Clear());
            // Independent module loads do not retain names from a prior game's pool.
            Check.That(GameRuntime.Load("samples/state-minimal").Resources!.Images.Count==0);
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
        foreach(var source in Directory.GetDirectories("samples").Where(p=>File.Exists(Path.Combine(p,GameRuntime.Entry))))
        {var loaded=GameRuntime.Load(source);GameRuntime.ValidateSnapshot(Wire.Read<LoadedModule>(Wire.Json(loaded)));}
    }
}
