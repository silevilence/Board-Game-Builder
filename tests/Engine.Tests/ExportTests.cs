using BoardGame.Modules;
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
        foreach(var file in new[]{"Play.exe","versions.json","Editor.dll"})
        {
            var target=path+"-tampered-"+file;
            Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",target,staging=>File.WriteAllText(Path.Combine(staging,file),"invalid")),file);
            Check.That(!Directory.Exists(target),"Broken export was published");
        }
    }
}
