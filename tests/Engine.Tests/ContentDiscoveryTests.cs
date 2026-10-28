using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ContentDiscoveryTests
{
    private sealed class Loader(string source) : IDefinitionLoader
    {
        public string Source=>source;
        public GameDefinition Load(string directory)=>Wire.Read<GameDefinition>(File.ReadAllText(DirectoryModule.SafeFile(directory,source)));
        public IReadOnlyList<string> ContentFiles(string directory)=>[source];
    }
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-discovery-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var plugin=new RuntimePluginDefinition("test.definition","1",1,["definition"],[],s=>
                s.Provide("definition",new Loader(s.Configuration.GetProperty("source").GetString()!)))
                {Content=new(["source","extra"],Prepare:true,Definition:c=>new(c.GetProperty("source").GetString()!,new Dictionary<string,string>()),
                    Files:c=>[c.GetProperty("extra").GetString()!])};
            var startup=new RuntimeStartup(1,"custom","1",[new(plugin.Id,"1",1,JsonSerializer.SerializeToElement(new{source="custom.json",extra="extra.txt"}))]);
            void Write()=>File.WriteAllText(Path.Combine(directory,GameRuntime.Entry),Wire.Json(startup));
            Write();File.WriteAllText(Path.Combine(directory,"extra.txt"),"content dependency");
            var template=GameRuntime.Load("samples/state-minimal").Definition!;
            File.WriteAllText(Path.Combine(directory,"custom.json"),Wire.Json(template));
            var loaded=GameRuntime.Load(directory,approvedPlugins:[plugin]);
            Check.That(loaded.RuntimeIdentity!.DefinitionSource=="custom.json" && loaded.RuntimeIdentity.ContentHashes.ContainsKey("extra.txt"));
            Check.That(Wire.Json(loaded.Initial)==Wire.Json(template.Initial));
            Check.That(loaded.RuntimeIdentity.Startup.Plugins.Count==1,"Host silently installed board plugins");
            var entry=GameRuntime.Load(Path.Combine(directory,GameRuntime.Entry),approvedPlugins:[plugin]);
            Check.That(entry.Fingerprint==loaded.Fingerprint);
            var package=Path.Combine(directory,"custom.bgm");GamePackage.Pack(directory,package,[plugin]);
            Check.That(GameRuntime.Load(package,approvedPlugins:[plugin]).Fingerprint==loaded.Fingerprint);
            var snapshot=Path.Combine(directory,"snapshot.json");File.WriteAllText(snapshot,Wire.Json(loaded));
            Check.That(GameRuntime.Load(snapshot,approvedPlugins:[plugin]).Fingerprint==loaded.Fingerprint);
            Check.Throws(()=>GameRuntime.Load(directory),"unapproved");
            startup.Plugins[0]=startup.Plugins[0] with {Configuration=JsonSerializer.SerializeToElement(new{source="custom.json",extra="../escape"})};Write();
            Check.Throws(()=>GameRuntime.Load(directory,approvedPlugins:[plugin]),"unsafe");
            startup.Plugins[0]=startup.Plugins[0] with {Configuration=JsonSerializer.SerializeToElement(new{source="custom.json",extra="extra.txt",unknown=true})};Write();
            Check.Throws(()=>GameRuntime.Load(directory,approvedPlugins:[plugin]),"unknown");
            startup.Plugins[0]=startup.Plugins[0] with {Version="2"};Write();
            Check.Throws(()=>GameRuntime.Load(directory,approvedPlugins:[plugin]),"version");
        }
        finally {Directory.Delete(directory,true);}
    }
}
