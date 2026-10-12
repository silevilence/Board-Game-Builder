using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Launcher;

internal static class InstallationChecks
{
    public static async Task Run(string root,byte[] fixture)
    {
        Directory.CreateDirectory(root); var registry=new InstallationRegistry(Path.Combine(root,"settings")); using var client=new HttpClient(); var installer=new InstallationService(registry,client);
        var first=Make(root,"0.2.0",fixture); var second=Make(root,"0.2.1",fixture);
        var installed=await installer.Import(first,first+".sha256"); Assert(installed.Installation.Managed,"managed import");
        await Task.WhenAll(installer.Import(second,second+".sha256"),installer.Import(second,second+".sha256"));
        Assert(registry.Inspect().Count==2 && registry.Inspect().All(i=>i.State=="available"),"concurrent idempotent import");
        Assert(Directory.EnumerateDirectories(Path.Combine(registry.Read().StorageRoot,"transactions")).Count()==0,"transactions cleaned");
        var bad=Path.Combine(root,"bad.zip");
        using(var zip=ZipFile.Open(bad,ZipArchiveMode.Create)) {using var output=zip.CreateEntry("../escape").Open();output.WriteByte(1);}
        Checksum(bad); await Reject(()=>installer.Import(bad,bad+".sha256")); Assert(!File.Exists(Path.Combine(root,"escape")),"archive traversal rejected");
        var third=Make(root,"0.2.2",fixture); using(var cancellation=new CancellationTokenSource())
        {
            var progress=new InlineProgress(p=> {if(p.Stage=="install") cancellation.Cancel();});
            try {await installer.Import(third,third+".sha256",progress,cancellation.Token);throw new Exception("Expected canceled installation");} catch(OperationCanceledException) { }
        }
        Assert(registry.Read().Installations.Length==2 && registry.Inspect().All(i=>i.State=="available"),"canceled install preserves old versions");
        var interrupted=await installer.Import(third,third+".sha256");
        var settings=registry.Read();
        File.WriteAllText(Path.Combine(registry.Home,"settings.json"),JsonSerializer.Serialize(settings with {Installations=settings.Installations.Where(i=>i.Version!="0.2.2").ToArray()},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var transaction=Path.Combine(settings.StorageRoot,"transactions",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(transaction);
        File.WriteAllText(Path.Combine(transaction,"journal.json"),JsonSerializer.Serialize(new {Version="0.2.2",ManifestHash=interrupted.Installation.ManifestHash}));
        await new InstallationService(new InstallationRegistry(registry.Home),client).Recover();
        Assert(!Directory.Exists(interrupted.Installation.Directory)&&registry.Inspect().All(i=>i.State=="available"),"restart rolls back unregistered committed directory");
        await installer.Import(third,third+".sha256"); Assert(registry.Read().Installations.Length==3,"retry after recovery");
        File.WriteAllText(first,"damaged ZIP"); await Reject(()=>installer.Import(first,first+".sha256"));
        Assert(registry.Inspect().All(i=>i.State=="available"),"corrupt import preserves old versions");
        Console.WriteLine("Launcher import/transaction/recovery checks passed.");
    }
    public static async Task Real(string downloads,string home)
    {
        var registry=new InstallationRegistry(home); using var client=new HttpClient(); var service=new InstallationService(registry,client);
        var results=new List<InstallationResult>();
        foreach(var version in new[]{"V0.2.0","V0.2.1"}) {var archive=Path.Combine(downloads,version,$"Board-Game-Builder-{version}-standalone-win-x64.zip");results.Add(await service.Import(archive,archive+".sha256"));}
        File.WriteAllText(Path.Combine(home,"import-results.json"),JsonSerializer.Serialize(new {results,physical=StorageDiagnostics.Measure(new ContentStore(registry.Read().StorageRoot))},new JsonSerializerOptions{WriteIndented=true}));
    }
    public static async Task Online(string home)
    {
        var registry=new InstallationRegistry(home);using var client=new HttpClient();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var catalog=await new ReleaseCatalog(client,home).Refresh(timeout.Token);
        if(catalog.Cached || catalog.Diagnostic!=null) throw new Exception("Actual online catalog unavailable: "+catalog.Diagnostic);
        var installer=new InstallationService(registry,client); var results=new List<InstallationResult>();
        foreach(var version in new[]{"0.2.0","0.2.1"}) results.Add(await installer.Install(catalog.Versions.Single(v=>v.Version==version),"private",cancellation:timeout.Token));
        if(results[1].Components.Length!=1 || !results[1].Components[0].StartsWith("product-",StringComparison.Ordinal)) throw new Exception("Unexpected repeated dependency download");
        File.WriteAllText(Path.Combine(home,"online-results.json"),JsonSerializer.Serialize(new {results,physical=StorageDiagnostics.Measure(new ContentStore(registry.Read().StorageRoot))},new JsonSerializerOptions{WriteIndented=true}));
    }
    public static void Compare(string first,string second)
    {
        var left=new InstallationRegistry(first);var right=new InstallationRegistry(second);
        foreach(var item in left.Read().Installations)
        {
            var other=right.Read().Installations.Single(i=>i.Version==item.Version);
            Assert(item.ManifestHash==other.ManifestHash && item.Mode==other.Mode,"online/import manifest equivalence");
            Assert(left.Inspect(item).State=="available" && right.Inspect(other).State=="available","online/import bytes equivalence");
        }
        Console.WriteLine("Actual online/import product equivalence passed.");
    }
    internal static string Make(string root,string version,byte[] fixture,bool native=false)
    {
        var node=JsonNode.Parse(fixture)!;node["version"]=version; var files=node["files"]!.AsArray();
        files.Add(new JsonObject{["path"]=".payload/coreclr.dll",["storage"]=".payload/coreclr.dll",["libraries"]=new JsonArray("runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12")});
        var contents=new Dictionary<string,byte[]>();
        foreach(var file in files)
        {
            var path=file!["path"]!.GetValue<string>(); var bytes=Encoding.UTF8.GetBytes(path.EndsWith("coreclr.dll",StringComparison.Ordinal)?"private runtime fixture":version+":"+path);
            if(native && path=="bgb.exe") bytes=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"));
            file["sha256"]=ProductManifest.Hash(bytes);file["size"]=bytes.Length;contents[path]=bytes;
        }
        var objects=files.Select(f=>f!["sha256"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        var identity=ProductManifest.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {group="product",objects},new JsonSerializerOptions{WriteIndented=true,NewLine="\n"})+"\n"));
        var id="product-"+identity;
        foreach(var file in files) file!["component"]=id;
        var component=node["components"]![0]!;component["id"]=id;component["objects"]=JsonSerializer.SerializeToNode(objects);component["source"]=new JsonObject{["repository"]=ReleaseCatalog.Repository,["tag"]="components-"+identity,["asset"]=id+".zip"};
        var componentRoot=Path.Combine(root,"components");Directory.CreateDirectory(componentRoot);var componentPath=Path.Combine(componentRoot,id+".zip");
        using(var zip=ZipFile.Open(componentPath,ZipArchiveMode.Create)) foreach(var bytes in contents.Values.DistinctBy(ProductManifest.Hash)) {using var output=zip.CreateEntry("objects/"+ProductManifest.Hash(bytes)).Open();output.Write(bytes);}
        component["size"]=new FileInfo(componentPath).Length;component["sha256"]=ProductManifest.HashFile(componentPath);
        var pathZip=Path.Combine(root,$"Board-Game-Builder-V{version}-standalone-win-x64.zip");
        using(var zip=ZipFile.Open(pathZip,ZipArchiveMode.Create))
        {
            foreach(var (path,content) in contents) {using var output=zip.CreateEntry(path).Open();output.Write(content);}
            using var metadata=zip.CreateEntry("toolkit.json").Open();metadata.Write(JsonSerializer.SerializeToUtf8Bytes(node));
        }
        Checksum(pathZip);return pathZip;
    }
    private static void Checksum(string zip)=>File.WriteAllText(zip+".sha256",ProductManifest.HashFile(zip)+"  "+Path.GetFileName(zip)+"\n");
    private static async Task Reject(Func<Task> action) {try {await action();} catch(InvalidDataException) {return;} throw new Exception("Expected invalid archive rejection");}
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
    private sealed class InlineProgress(Action<TransferProgress> action):IProgress<TransferProgress> {public void Report(TransferProgress value)=>action(value);}
}
