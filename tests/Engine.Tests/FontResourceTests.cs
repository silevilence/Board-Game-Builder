using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;

namespace Engine.Tests;

public static class FontResourceTests
{
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-font-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/state-minimal",folder);
            var asset="samples/cursed-investigators/assets/fonts/noto-sans-cjk-sc/";
            File.Copy(asset+"NotoSansCJKsc-Regular.otf",Path.Combine(folder,"font.otf"));
            File.Copy(asset+"LICENSE.txt",Path.Combine(folder,"LICENSE.txt"));
            var entry=Path.Combine(folder,GameRuntime.Entry);var startup=JsonNode.Parse(File.ReadAllText(entry))!;
            var plugins=startup["plugins"]!.AsArray();
            plugins.Add(new JsonObject{["id"]="resources",["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
            var font=new JsonObject{["path"]="font.otf",["license"]="LICENSE.txt"};
            plugins.Add(new JsonObject{["id"]="resources.font",["version"]="1",["api"]=1,["configuration"]=font});
            void Save()=>File.WriteAllText(entry,startup.ToJsonString());
            Save();var module=GameRuntime.Load(folder);
            Check.That(module.Resources!.Font==new FontReference("font.otf","LICENSE.txt"));
            var scene=TableScene.From(new GameEngine(module.Initial).Project("p1"),module.Resources);
            Check.That(scene.Resources.Font==module.Resources.Font && scene.Resources.Files.ContainsKey("font.otf"),"Projection dropped the font");
            var package=Path.Combine(folder,"font.bgm");GamePackage.Pack(folder,package);
            Check.That(GameRuntime.Load(package).Fingerprint==module.Fingerprint);
            var snapshot=Wire.Read<LoadedModule>(Wire.Json(module));GameRuntime.ValidateSnapshot(snapshot);
            font["license"]="missing.txt";Save();Check.Throws(()=>GameRuntime.Load(folder),"missing");
            font["license"]="LICENSE.txt";font["path"]="../outside.otf";Save();Check.Throws(()=>GameRuntime.Load(folder),"path");
            font["path"]="font.otf";Save();
            File.WriteAllBytes(Path.Combine(folder,"font.otf"),[0,1,0,0]);Check.Throws(()=>GameRuntime.Load(folder),"font.otf");
            File.Delete(Path.Combine(folder,"font.otf"));File.Delete(Path.Combine(folder,"LICENSE.txt"));
            Check.That(GameRuntime.Load(package).Resources!.Font!=null,"Package depends on source files");
            GameRuntime.ValidateSnapshot(snapshot);
            Check.That(GameRuntime.Load("samples/state-minimal").Resources!.Font==null,"Font leaked to another game");
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
