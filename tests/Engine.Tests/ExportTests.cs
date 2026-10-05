using BoardGame.Modules;
namespace Engine.Tests;
public static class ExportTests
{
    public static void Run()
    {
        const string release="artifacts/player-release-m2";
        var path=Path.GetFullPath("artifacts/export-test-"+Guid.NewGuid());
        PlayerDistribution.Export(release,"samples/quick-cards",path,staging=>File.WriteAllText(Path.Combine(staging,"extra.txt"),"export step"));
        Check.That(File.Exists(Path.Combine(path,"Play.exe")) && !File.Exists(Path.Combine(path,"Play.cmd")) && File.Exists(Path.Combine(path,"versions.json")));
        Check.That(File.ReadAllText(Path.Combine(path,"extra.txt"))=="export step");
        Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",path),"already exists");
        Check.Throws(()=>PlayerDistribution.Export(release,"samples/quick-cards",path+"-fail",_=>throw new Exception("plugin failure")),"plugin failure");
        Check.That(!Directory.Exists(path+"-fail"));
        Check.Throws(()=>PlayerDistribution.Preflight(path+"-absent","samples/quick-cards"));
    }
}
