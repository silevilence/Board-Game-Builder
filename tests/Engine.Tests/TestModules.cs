using System.IO.Compression;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

// Kernel/Lua unit suites intentionally mutate source payloads without a signed-in runtime identity.
// Runtime integration suites use GameRuntime directly and test the complete immutable startup identity.
public static class TestModules
{
    public static LoadedModule Load(string source)
    {
        LoadedModule module;
        if(Directory.Exists(source)) module=File.Exists(Path.Combine(source,GameRuntime.Entry))?GameRuntime.Load(source):DirectoryModule.Load(source);
        else if(Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase))
        {
            using var zip=ZipFile.OpenRead(source);
            var manifest=zip.GetEntry("package.manifest.json");
            if(manifest==null) return ModulePackage.Load(source);
            using var reader=new StreamReader(manifest.Open());
            var format=JsonNode.Parse(reader.ReadToEnd())!["format"]!.GetValue<int>();
            module=format==2?GameRuntime.Load(source):ModulePackage.Load(source);
        }
        else module=ModuleSnapshot.Load(source);
        if(module.RuntimeIdentity==null) return module;
        module=module with {RuntimeIdentity=null,Definition=null};
        return module with {Fingerprint=ModuleSnapshot.Fingerprint(module)};
    }
    public static void Pack(string source,string destination)
    {if(File.Exists(Path.Combine(source,GameRuntime.Entry))) GamePackage.Pack(source,destination);else ModulePackage.Pack(source,destination);}
    public static void CopyGame(string source,string destination)
    {
        if(!File.Exists(Path.Combine(source,GameRuntime.Entry))) {GameMigration.Migrate(source,destination);return;}
        Copy(source,destination);
    }
    public static void CopyLegacy(string source,string destination)
    {
        Copy(source,destination);
        if(!File.Exists(Path.Combine(source,GameRuntime.Entry))) return;
        var startup=GameRuntime.ReadStartup(source);
        var definition=startup.Plugins.Single(p=>p.Id=="definition.module").Configuration.GetProperty("path").GetString()!;
        var root=JsonNode.Parse(File.ReadAllText(Path.Combine(source,definition)))!.AsObject();
        root["id"]=startup.Id;root["version"]=startup.Version;
        root["rules"]=startup.Plugins.Single(p=>p.Id=="lua.rules").Configuration.GetProperty("file").GetString();
        root["cpu"]=startup.Plugins.Single(p=>p.Id=="lua.cpu").Configuration.GetProperty("file").GetString();
        var wasm=startup.Plugins.FirstOrDefault(p=>p.Id=="wasm");
        if(wasm!=null) root["plugins"]=JsonNode.Parse(wasm.Configuration.GetProperty("extensions").GetRawText());
        File.WriteAllText(Path.Combine(destination,"module.json"),root.ToJsonString());File.Delete(Path.Combine(destination,GameRuntime.Entry));
    }
    private static void Copy(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            var target=Path.Combine(destination,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
        }
    }
}
