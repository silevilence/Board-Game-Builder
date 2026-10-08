using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

public static class GameMigration
{
    // Explicit conversion only; normal loading never invokes the legacy parser.
    public static void Migrate(string source,string destination)
    {
        var original=DirectoryModule.Load(source);
        if(Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new migration destination");
        var root=JsonNode.Parse(File.ReadAllText(DirectoryModule.SafeFile(source,"module.json")))!.AsObject();
        var files=ModulePackage.ReferencedFiles(root).ToDictionary(p=>p,p=>File.ReadAllBytes(DirectoryModule.SafeFile(source,p)),StringComparer.Ordinal);
        var plugins=new List<RuntimePluginSelection>
        {
            Select("kernel",new {}),Select("session.local",new {}),
            Select("definition.module",new {path="module.json"}),
            Select("lua.rules",new {file=root["rules"]!.GetValue<string>()}),Select("lua.cpu",new {file=root["cpu"]!.GetValue<string>()})
        };
        var wasm=ModulePackage.Dependencies(root).Where(p=>p.Kind=="wasm").ToArray();
        if(wasm.Length>0) plugins.Add(Select("wasm",new {extensions=wasm}));
        foreach(var key in new[]{"id","version","rules","cpu","plugins","api"}) root.Remove(key);
        files["module.json"]=Encoding.UTF8.GetBytes(root.ToJsonString(new(){WriteIndented=true}));
        files[GameRuntime.Entry]=Encoding.UTF8.GetBytes(Wire.Json(new RuntimeStartup(1,original.Id,original.Version,plugins)));
        var target=Path.GetFullPath(destination);var staging=target+".migrating-"+Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(staging);
            foreach(var file in files)
            {
                var output=Path.Combine(staging,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllBytes(output,file.Value);
            }
            var migrated=GameRuntime.LoadDirectory(staging);
            if(Wire.Json(migrated.Initial)!=Wire.Json(original.Initial)) throw new InvalidDataException("Migration changed the initial state");
            Directory.Move(staging,target);
        }
        finally {if(Directory.Exists(staging)) Directory.Delete(staging,true);}
    }
    internal static RuntimePluginSelection Select(string id,object configuration)=>new(id,"1",1,JsonSerializer.SerializeToElement(configuration,Wire.Options));
}
