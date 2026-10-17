using BoardGame.Runtime;
namespace Engine.Tests;
public static class ExportTests
{
    public static void Run()
    {
        var release=Environment.GetEnvironmentVariable("BGB_TEST_RELEASE") ?? "artifacts/player-release-m2";
        var path=Path.GetFullPath("artifacts/export-test-"+Guid.NewGuid());
        PlayerDistribution.Export(release,"samples/quick-cards",path,staging=>File.WriteAllText(Path.Combine(staging,"extra.txt"),"export step"));
        Check.That(File.Exists(Path.Combine(path,"Play.exe")) && !File.Exists(Path.Combine(path,"Play.cmd")) && File.Exists(Path.Combine(path,"versions.json")));
        Check.That(File.ReadAllText(Path.Combine(path,"extra.txt"))=="export step");
        Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",path),"already exists");
        Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",path+"-fail",_=>throw new Exception("plugin failure")),"plugin failure");
        Check.That(!Directory.Exists(path+"-fail"));
        Check.Throws(()=>PlayerDistribution.Preflight(path+"-absent","samples/quick-cards"));
        var manifestPath=Path.Combine(path,"release.json");var original=File.ReadAllText(manifestPath);
        foreach(var missing in new[]{"Runtime.dll","MonoGame.Framework.dll","worker/MoonSharp.Interpreter.dll"})
        {
            var broken=System.Text.Json.Nodes.JsonNode.Parse(original)!;broken["files"]!.AsObject().Remove(missing);
            File.WriteAllText(manifestPath,broken.ToJsonString());Check.Throws(()=>PlayerDistribution.Preflight(path,"samples/quick-cards"),missing);
        }
        var catalog=System.Text.Json.Nodes.JsonNode.Parse(original)!;catalog["runtimePlugins"]!.AsObject().Remove("kernel");
        File.WriteAllText(manifestPath,catalog.ToJsonString());Check.Throws(()=>PlayerDistribution.Preflight(path,"samples/quick-cards"),"kernel");
        File.WriteAllText(manifestPath,original);
        foreach(var file in new[]{"Play.exe","versions.json","Editor.dll","CordiSharp.dll","game.bgm"})
        {
            var target=path+"-tampered-"+file;
            Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",target,staging=>File.WriteAllText(Path.Combine(staging,file),"invalid")),file);
            Check.That(!Directory.Exists(target),"Broken export was published");
        }
    }
    public static void LargePackage()
    {
        var release=Environment.GetEnvironmentVariable("BGB_TEST_RELEASE") ?? "artifacts/player-release-m2";
        var target=Path.GetFullPath("artifacts/export-large-test-"+Guid.NewGuid());
        try
        {
            // Individually valid scanned resources produce a package above 32 MiB.
            PlayerDistribution.Export(release,"samples/cursed-investigators",target);
            var package=Path.Combine(target,"game.bgm");
            Check.That(new FileInfo(package).Length>BoardGame.Modules.ModuleContentLimits.MaxFileBytes);
            Check.That(GameRuntime.Load(package).Fingerprint==GameRuntime.Load("samples/cursed-investigators").Fingerprint);
        }
        finally {if(Directory.Exists(target))Directory.Delete(target,true);}
    }
}
