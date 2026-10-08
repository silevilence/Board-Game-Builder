using System.IO.Compression;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class PackageTests
{
    public static void Run()
    {
        var root=Path.GetFullPath("artifacts/package-tests/"+Guid.NewGuid()); Directory.CreateDirectory(root);
        var source=Path.Combine(root,"source"); Directory.CreateDirectory(source);
        TestModules.CopyLegacy("samples/quick-cards",source);
        var config=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"module.json")))!.AsObject();
        config["plugins"]=JsonNode.Parse("""[{"id":"lua","version":"2.0.0","api":1},{"id":"private-editor","version":"1","api":999,"editor_only":true}]""");
        File.WriteAllText(Path.Combine(source,"module.json"),config.ToJsonString());
        File.WriteAllText(Path.Combine(source,"editor-only-secret.txt"),"must not be exported");
        var package=Path.Combine(root,"game.bgm"); TestModules.Pack(source,package);
        var directory=TestModules.Load(source); var packed=TestModules.Load(package);
        Check.That(directory.Fingerprint==packed.Fingerprint);
        using var a=new PlaySession(directory,42,directory.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        using var b=new PlaySession(packed,42,packed.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        while(!a.Ended && a.Fault==null) a.StepCpu(); while(!b.Ended && b.Fault==null) b.StepCpu();
        Check.That(a.Ended && b.Ended && a.AuthorityStateHash()==b.AuthorityStateHash());
        Check.That(Wire.Json(GameEngine.Replay(packed,a.ExportAuthorityLog()).Project("p1"))==Wire.Json(a.View("p1")));
        using(var zip=ZipFile.OpenRead(package)) Check.That(!zip.Entries.Any(e=>e.FullName.Contains("editor")));
        var corrupt=Path.Combine(root,"corrupt.bgm"); File.Copy(package,corrupt);
        using(var zip=ZipFile.Open(corrupt,ZipArchiveMode.Update))
        { zip.GetEntry("rules.lua")!.Delete(); using var writer=new StreamWriter(zip.CreateEntry("rules.lua").Open()); writer.Write("return {}"); }
        Check.Throws(()=>TestModules.Load(corrupt),"hash mismatch");
        var traversal=Path.Combine(root,"traversal.bgm");
        using(var zip=ZipFile.Open(traversal,ZipArchiveMode.Create)) zip.CreateEntry("../outside.txt");
        Check.Throws(()=>TestModules.Load(traversal),"unsafe package path");
        foreach(var deps in new[] {
            """[{"id":"absent","version":"1","api":1}]""",
            """[{"id":"lua","version":"9","api":1}]""",
            """[{"id":"lua","version":"2.0.0","api":9}]""",
            """[{"id":"lua","version":"2.0.0","api":1},{"id":"lua","version":"2.0.0","api":1}]""" })
        {
            config["plugins"]=JsonNode.Parse(deps); File.WriteAllText(Path.Combine(source,"module.json"),config.ToJsonString());
            Check.Throws(()=>TestModules.Load(source),"plugins");
        }
    }
}
