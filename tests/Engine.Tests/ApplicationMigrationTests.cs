using System.Text;
using System.Text.Json;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ApplicationMigrationTests
{
    public const string Inventory="docs/validation/application-entry-migration.json";
    public static LoadedModule Legacy(string sample)
    {
        var path="samples/"+sample;
        using var inventory=JsonDocument.Parse(File.ReadAllText(Inventory));
        var entry=inventory.RootElement.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("path").GetString()==path+"/game.start.json");
        var files=ProjectPaths.Read(path);files[GameRuntime.Entry]=Encoding.UTF8.GetBytes(entry.GetProperty("before").GetString()!);
        using var snapshot=new ProjectSnapshot(new(new(Path.GetFullPath(path),"legacy-baseline","memory"),files));
        return GameRuntime.Load(snapshot.Directory);
    }
    public static void Run()=>Verify().GetAwaiter().GetResult();
    private static async Task Verify()
    {
        using var inventory=JsonDocument.Parse(File.ReadAllText(Inventory));
        var entries=inventory.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Check.That(entries.Select(e=>e.GetProperty("path").GetString()).Order().SequenceEqual(Directory.GetFiles("samples","game.start.json",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Order()),"Uninventoried sample entry");
        foreach(var entry in entries)
        {
            var path=entry.GetProperty("path").GetString()!;
            var migrated=entry.GetProperty("action").GetString()=="explicit-board-render";
            var normalized=Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n","\n",StringComparison.Ordinal));
            Check.That(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(normalized))==entry.GetProperty(migrated?"afterTextSha256":"textSha256").GetString(),"Unexpected entry changes: "+path);
            if(!migrated)continue;
            var directory=Path.GetDirectoryName(path)!;var module=GameRuntime.Load(directory);
            _=BoardRenderContract.Require(module.RuntimeIdentity!.Startup);
            var old=Legacy(Path.GetFileName(directory));
            Check.Throws(()=>BoardRenderContract.Require(old.RuntimeIdentity!.Startup),"explicitly migrate");
            Check.That(Wire.Json(old.Initial)==Wire.Json(module.Initial) && old.Rules==module.Rules && old.Cpu==module.Cpu,"Migration changed game content: "+path);
            var package=Path.Combine(Path.GetTempPath(),"migration-"+Guid.NewGuid()+".bgm");
            try
            {
                GamePackage.Pack(directory,package);Check.That(GameRuntime.Load(package).Fingerprint==module.Fingerprint,"Package differs: "+path);
                await using var host=await AuthorHost.Start(AuthorProfiles.Resolve(Path.GetFullPath(directory),null));
                var valid=(ValidationResult)await host.Invoke("project.validate",JsonSerializer.SerializeToElement(new{layer="dependencies"}),"migration");
                Check.That(valid.Success,path+": "+string.Join(";",valid.Diagnostics.Select(d=>d.Message)));
            }
            finally {if(File.Exists(package))File.Delete(package);}
            Console.WriteLine("Migrated directory/package/author: "+directory);
        }
        var template=GameAuthorFormats.Template();var generated=new ProjectView(new("","template","memory"),template);
        var startup=JsonSerializer.Deserialize<RuntimeStartup>(generated.Text(GameRuntime.Entry),Wire.Options)!;
        _=BoardRenderContract.Require(startup);
        Check.That(startup.Plugins.Select(p=>p.Id).Distinct().Count()==startup.Plugins.Count);
        Check.That(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BoardGame.Presentation"),"Headless migration loaded graphical implementation");
    }
}
