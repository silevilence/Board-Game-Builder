using BoardGame.Launcher;

internal static class ProjectVersionChecks
{
    public static void Run(string root,InstallationRegistry registry)
    {
        var selector=new ProjectVersions(registry);registry.SetDefault("0.2.1");
        foreach(var format in new[]{"module","state"})
        {
            var project=Path.Combine(root,format);Directory.CreateDirectory(project);
            var entry=Path.Combine(project,"game.start.json");var bytes=System.Text.Encoding.UTF8.GetBytes("{\"formatFixture\":\""+format+"\"}");File.WriteAllBytes(entry,bytes);
            var child=Path.Combine(project,"规则 子目录");Directory.CreateDirectory(child);
            Assert(selector.Resolve(project:project).Version=="0.2.1","unbound default");
            selector.Bind(project,"V0.2.0");var selection=selector.Resolve(currentDirectory:child);
            Assert(selection.Version=="0.2.0"&&selection.Source=="project"&&selection.Project==project,"ancestor binding");
            Assert(selector.Resolve("0.2.1",project).Source=="explicit"&&ProjectVersions.Read(project)?.Version=="0.2.0","temporary override preserves binding");
            registry.SetDefault("0.2.1");Assert(selector.Resolve(project:project).Version=="0.2.0","default change cannot change binding");
            selector.Bind(project,"0.9.0");selection=selector.Resolve(project:project);
            Assert(selection.State=="missing"&&selection.Version=="0.9.0","missing binding never falls back");
            try {selection.RequireAvailable();throw new Exception("missing accepted");}catch(InvalidOperationException) { }
            Assert(selector.Resolve("0.2.0",project).State=="available","explicit override missing binding");
            Assert(File.ReadAllBytes(entry).SequenceEqual(bytes),"game content preserved");
            selector.Bind(project,null);Assert(selector.Resolve(project:entry).Source=="default","clear binding explicit");
            File.WriteAllText(Path.Combine(project,ProjectVersions.FileName),"{\"api\":999,\"version\":\"0.2.0\"}");
            try {selector.Resolve(project:project);throw new Exception("unknown binding accepted");}catch(InvalidDataException) { }
        }
        Console.WriteLine("Launcher author version selection/binding checks passed.");
    }
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
}
