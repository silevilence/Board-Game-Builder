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
        var legacy=directory+"-legacy";
        try
        {
            TestModules.CopyLegacy("samples/quick-cards",legacy);
            Check.Throws(()=>GameRuntime.Load(legacy),"migrate");
            GameMigration.Migrate(legacy,directory);
            var module=GameRuntime.Load(directory);GamePackage.Pack(directory,package);
            var packed=GameRuntime.Load(package);
            Check.That(Wire.Json(module.RuntimeIdentity)==Wire.Json(packed.RuntimeIdentity),"Directory/package plugin selection differs");
            Check.That(module.Fingerprint==packed.Fingerprint);
            void Tampered(Action<System.IO.Compression.ZipArchive> alter,string message)
            {
                var damaged=package+".damaged";
                try
                {
                    File.Copy(package,damaged);
                    using(var zip=System.IO.Compression.ZipFile.Open(damaged,System.IO.Compression.ZipArchiveMode.Update)) alter(zip);
                    Check.Throws(()=>GamePackage.Load(damaged),message);
                }
                finally {File.Delete(damaged);}
            }
            Tampered(zip=>zip.CreateEntry("undeclared.txt"),"file count");
            Tampered(zip=>zip.CreateEntry("../escape.txt"),"unsafe");
            Tampered(zip=>zip.CreateEntry("RULES.LUA"),"duplicate");
            Tampered(zip=>
            {
                zip.GetEntry("rules.lua")!.Delete();using var writer=new StreamWriter(zip.CreateEntry("rules.lua").Open());writer.Write("tampered");
            },"hash mismatch");
            Tampered(zip=>
            {
                zip.GetEntry("package.manifest.json")!.Delete();using var writer=new StreamWriter(zip.CreateEntry("package.manifest.json").Open());
                writer.Write("""{"format":1,"fingerprint":"old","files":{}}""");
            },"Legacy/incompatible");
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
            Broken(root=>
            {
                var rules=root["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="lua.rules")!;
                rules["id"]="rules.commands";rules["configuration"]=JsonNode.Parse("""{"actions":[null]}""");
            },"action schema");
            var definition=Path.Combine(directory,"module.json");var text=File.ReadAllText(definition);
            var duplicate=JsonNode.Parse(text)!;duplicate["id"]="conflict";File.WriteAllText(definition,duplicate.ToJsonString());
            Check.Throws(()=>GameRuntime.Load(directory),"duplicate configuration");File.WriteAllText(definition,text);
            File.Move(definition,Path.Combine(directory,"another.json"));
            var relocated=JsonNode.Parse(original)!;relocated["plugins"]![2]!["configuration"]!["path"]="another.json";
            File.WriteAllText(path,relocated.ToJsonString());
            Check.That(Wire.Json(GameRuntime.Load(directory).Initial)==Wire.Json(module.Initial),"Loader ignored configured definition path");
            using(var large=File.Open(Path.Combine(directory,"rules.lua"),FileMode.Open)) large.SetLength(32L*1024*1024+1);
            Check.Throws(()=>GameRuntime.Load(directory),"size budget");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);if(Directory.Exists(legacy)) Directory.Delete(legacy,true);File.Delete(package);}
    }
}
