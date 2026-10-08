using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class StartupTests
{
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-startup-"+Guid.NewGuid());
        var package=directory+".bgm";
        try
        {
            Check.Throws(()=>GameRuntime.Load("samples/quick-cards"),"migrate");
            GameMigration.Migrate("samples/quick-cards",directory);
            var module=GameRuntime.Load(directory);GamePackage.Pack(directory,package);
            var packed=GameRuntime.Load(package);
            Check.That(Wire.Json(module.RuntimeIdentity)==Wire.Json(packed.RuntimeIdentity),"Directory/package plugin selection differs");
            Check.That(module.Fingerprint==packed.Fingerprint);
            using var session=new PlaySession(packed);Check.That(session.Actions().Count>0);
            var path=Path.Combine(directory,GameRuntime.Entry);var original=File.ReadAllText(path);
            void Broken(Action<JsonNode> mutate,string message)
            {
                var root=JsonNode.Parse(original)!;mutate(root);File.WriteAllText(path,root.ToJsonString());
                Check.Throws(()=>GameRuntime.Load(directory),message);File.WriteAllText(path,original);
            }
            Broken(root=>root["format"]=2,"format");
            Broken(root=>root["plugins"]![0]!["version"]="999","version");
            Broken(root=>root["plugins"]![0]!["id"]="arbitrary.dll","unapproved");
            Broken(root=>root["plugins"]![2]!["configuration"]!["path"]="../outside.json","unsafe");
            Broken(root=>root["plugins"]![2]!["configuration"]!["typo"]=true,"unknown configuration");
            Broken(root=>root["plugins"]!.AsArray().RemoveAt(0),"required capability");
            Broken(root=>root["plugins"]!.AsArray().Add(root["plugins"]![2]!.DeepClone()),"duplicate");
            var definition=Path.Combine(directory,"module.json");var text=File.ReadAllText(definition);
            var duplicate=JsonNode.Parse(text)!;duplicate["id"]="conflict";File.WriteAllText(definition,duplicate.ToJsonString());
            Check.Throws(()=>GameRuntime.Load(directory),"duplicate configuration");File.WriteAllText(definition,text);
            File.Move(definition,Path.Combine(directory,"another.json"));
            var relocated=JsonNode.Parse(original)!;relocated["plugins"]![2]!["configuration"]!["path"]="another.json";
            File.WriteAllText(path,relocated.ToJsonString());
            Check.That(Wire.Json(GameRuntime.Load(directory).Initial)==Wire.Json(module.Initial),"Loader ignored configured definition path");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);File.Delete(package);}
    }
}
