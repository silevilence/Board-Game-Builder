using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Launcher;

internal static class ContentChecks
{
    public static async Task Run(string root,byte[] fixture)
    {
        var node=JsonNode.Parse(fixture)!; var objects=node["files"]!.AsArray().ToDictionary(f=>f!["sha256"]!.GetValue<string>(),f=>Encoding.UTF8.GetBytes(f!["path"]!.GetValue<string>()));
        using var bytes=new MemoryStream();
        using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true)) foreach(var (hash,content) in objects) {using var output=zip.CreateEntry("objects/"+hash).Open(); output.Write(content);}
        var archive=bytes.ToArray(); node["components"]![0]!["size"]=archive.Length; node["components"]![0]!["sha256"]=ProductManifest.Hash(archive); node["components"]![0]!["source"]!["repository"]=ReleaseCatalog.Repository;
        var manifest=ProductManifest.Parse(JsonSerializer.SerializeToUtf8Bytes(node));
        var store=new ContentStore(root); using var handler=new ArchiveHandler(archive); using var client=new HttpClient(handler);
        using(var operation=await store.Begin()) {await operation.Fetch(manifest,client); Assert(operation.DownloadedBytes==archive.Length,"first transfer"); operation.Commit();}
        var originalRequests=handler.Requests;
        await Task.WhenAll(Enumerable.Range(0,2).Select(async _=> {using var operation=await store.Begin(); await operation.Fetch(manifest,client); operation.Commit();}));
        Assert(handler.Requests==originalRequests && store.Plan(manifest).MissingObjects==0,"concurrent verified reuse");
        var canceledStore=new ContentStore(root+"-canceled");
        using(var cancellation=new CancellationTokenSource())
        using(var operation=await canceledStore.Begin())
        {
            cancellation.Cancel(); try {await operation.Fetch(manifest,client,cancellation:cancellation.Token); throw new Exception("Expected cancellation");} catch(OperationCanceledException) { }
        }
        Assert(!Directory.EnumerateFiles(root+"-canceled","*",SearchOption.AllDirectories).Any(p=>Path.GetExtension(p)==".tmp"),"cancellation cleanup");
        var badStore=new ContentStore(root+"-bad"); handler.Corrupt=true;
        using(var operation=await badStore.Begin()) {try {await operation.Fetch(manifest,client); throw new Exception("Expected corrupt archive rejection");} catch(InvalidDataException) { }}
        Assert(!Directory.EnumerateFiles(Path.Combine(root+"-bad","objects")).Any(),"corrupt archive publishes no objects");
        var one=manifest.Files[0]; File.SetAttributes(store.ObjectPath(one.Sha256),FileAttributes.Normal); File.WriteAllText(store.ObjectPath(one.Sha256),"corrupt"); handler.Corrupt=false;
        using(var operation=await store.Begin()) {try {await operation.Fetch(manifest,client); throw new Exception("Expected corrupt store rejection");} catch(InvalidDataException) { }}
        Assert(store.Plan(manifest).MissingObjects==1,"corrupt object not treated as verified");
        using(var operation=await store.Begin()) {try {await operation.Add(one with {Sha256=new string('b',64),Size=long.MaxValue},new MemoryStream()); throw new Exception("Expected insufficient space rejection");} catch(IOException) { }}
        try {using var unsupported=await new ContentStore(@"\\localhost\unsupported").Begin(); throw new Exception("Expected unsupported storage rejection");} catch(IOException) { }
        Console.WriteLine("Launcher shared content/concurrency/cancellation checks passed.");
    }
    public static async Task Real(string downloads,string storage,bool online=false)
    {
        var store=new ContentStore(storage); using var handler=new LocalArchives(Path.Combine(downloads,"components")); using var client=online?new HttpClient():new HttpClient(handler);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var reports=new List<object>();
        foreach(var version in new[]{"V0.2.0","V0.2.1"})
        {
            var manifest=ProductManifest.Read(Path.Combine(downloads,version,"toolkit-standalone.json"));
            using var operation=await store.Begin(timeout.Token); var plan=store.Plan(manifest); await operation.Fetch(manifest,client,cancellation:timeout.Token);
            var directory=ProductManifest.SafePath(storage,"versions/"+version); Directory.CreateDirectory(directory);
            foreach(var file in manifest.Files) {var path=ProductManifest.SafePath(directory,file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); ContentStore.Link(path,store.ObjectPath(file.Sha256));}
            File.WriteAllBytes(Path.Combine(directory,"toolkit.json"),manifest.Bytes); manifest.VerifyDirectory(directory); operation.Commit();
            reports.Add(new {version,online,plan.ReusedObjects,plan.MissingObjects,operation.DownloadedBytes,operation.RequestedComponents,logicalBytes=manifest.Files.Sum(f=>f.Size),uniqueBytes=Directory.EnumerateFiles(Path.Combine(storage,"objects")).Sum(p=>new FileInfo(p).Length)});
            if(version=="V0.2.1" && plan.Downloads.Any(c=>c.Kind!="product")) throw new Exception("Unchanged runtime/dependencies downloaded twice");
        }
        File.WriteAllText(Path.Combine(storage,"measurements.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
    private sealed class ArchiveHandler(byte[] archive):HttpMessageHandler
    {
        public int Requests; public bool Corrupt;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++; var content=archive.ToArray(); if(Corrupt) content[0]^=1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(content)});
        }
    }
    private sealed class LocalArchives(string root):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(File.OpenRead(Path.Combine(root,Path.GetFileName(request.RequestUri!.AbsolutePath))))});
        }
    }
}
