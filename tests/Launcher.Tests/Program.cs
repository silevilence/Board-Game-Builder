using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Launcher;

if(args is ["--store",var downloads,var storage]) {await ContentChecks.Real(downloads,storage);return 0;}
if(args is ["--store-online",var onlineDownloads,var onlineStorage]) {await ContentChecks.Real(onlineDownloads,onlineStorage,true);return 0;}
if(args is ["--measure",var storeRoot]) {Console.WriteLine(JsonSerializer.Serialize(StorageDiagnostics.Measure(new ContentStore(storeRoot)),new JsonSerializerOptions{WriteIndented=true}));return 0;}
var root=Path.Combine(Path.GetTempPath(),"bgb-launcher-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    Check(ProductManifest.NormalizeVersion("v0.10.0")=="0.10.0","version prefix");
    Reject(()=>ProductManifest.NormalizeVersion("VV0.2.0")); Reject(()=>ProductManifest.NormalizeVersion("0.02.0"));
    var first=Fixture(Path.Combine(root,"中文 版本一"),"0.2.0"); var second=Fixture(Path.Combine(root,"版本二"),"0.2.1");
    ProductManifest.Read(Path.Combine(first,"toolkit.json")).VerifyDirectory(first);
    var bytes=File.ReadAllBytes(Path.Combine(first,"toolkit.json"));
    foreach(var change in new Action<JsonNode>[] {
        n=>n["api"]=999, n=>n["platform"]="linux-x64", n=>n["entries"]!["cli"]="other.exe",
        n=>n["files"]![0]!["path"]="../escape", n=>n["files"]![0]!["storage"]="missing",
        n=>n["components"]![0]!["source"]!["tag"]="latest", n=>n["programs"]![0]!["frameworks"]![0]!["rollForward"]="Major",
        n=>n["programs"]![0]!["files"]=new JsonArray(), n=>n["files"]!.AsArray().Add(n["files"]![0]!.DeepClone()) })
    { var node=JsonNode.Parse(bytes)!; change(node); Reject(()=>ProductManifest.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()))); }
    Reject(()=>ProductManifest.Parse(Encoding.UTF8.GetBytes("{\"api\":1,\"api\":2}")));
    await CatalogChecks.Run(Path.Combine(root,"catalog"),bytes);
    await ContentChecks.Run(Path.Combine(root,"shared"),bytes);
    var home=Path.Combine(root,"settings"); var registry=new InstallationRegistry(home);
    Check(registry.Read().Installations.Length==0,"empty startup"); registry.Register(first); registry.Register(second); registry.SetDefault("v0.2.1");
    var restarted=new InstallationRegistry(home); Check(restarted.Read().DefaultVersion=="0.2.1"&&restarted.Inspect().All(i=>i.State=="available"),"persisted installations/default");
    Check(restarted.Register(first).Version=="0.2.0"&&restarted.Read().Installations.Length==2,"idempotent registration");
    var conflict=Fixture(Path.Combine(root,"conflict"),"0.2.0"); Reject(()=>restarted.Register(conflict));
    File.WriteAllText(Path.Combine(first,"bgb.exe"),"damaged"); Check(restarted.Inspect().Single(i=>i.Installation.Version=="0.2.0").State=="corrupt","corrupt state"); Reject(()=>restarted.SetDefault("0.2.0"));
    Directory.Move(second,second+"-moved"); Check(restarted.Inspect().Single(i=>i.Installation.Version=="0.2.1").State=="missing","missing state");
    var legacy=Path.Combine(root,"legacy"); Directory.CreateDirectory(legacy);
    foreach(var entry in ProductManifest.RequiredEntries.Values) { var path=Path.Combine(legacy,entry); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,"legacy fixture"); }
    File.WriteAllText(Path.Combine(legacy,"toolkit.json"),JsonSerializer.Serialize(new {api=1,version="0.1.0",platform="win-x64",commit="fixture",entries=ProductManifest.RequiredEntries}));
    Check(restarted.Register(legacy).Mode=="legacy","explicit legacy registration");
    foreach(var input in args) { var manifest=ProductManifest.Read(input); Console.WriteLine($"Real manifest: {manifest.Version} {manifest.Mode}, {manifest.Files.Count} files, {manifest.Components.Count} components"); }
    Console.WriteLine("Launcher manifest/registry checks passed."); return 0;
}
catch(Exception e) { Console.Error.WriteLine(e); return 1; }
finally { if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) {foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)) File.SetAttributes(file,FileAttributes.Normal); Directory.Delete(root,true);} }

static void Check(bool value,string name) { if(!value) throw new Exception("Failed: "+name); }
static void Reject(Action action) { try { action(); } catch(Exception e) when(e is InvalidDataException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected rejection"); }
static string Fixture(string root,string version)
{
    Directory.CreateDirectory(root);
    var paths=ProductManifest.RequiredEntries.Values.Concat(ProductManifest.RequiredEntries.Where(e=>e.Key!="documentation").Select(e=>".payload/"+e.Value)).ToArray();
    var files=paths.Select(path=>new {path,size=(long)Encoding.UTF8.GetByteCount(path),sha256=ProductManifest.Hash(Encoding.UTF8.GetBytes(path)),storage=path,libraries=Array.Empty<string>()}).ToArray();
    foreach(var file in files) { var path=Path.Combine(root,file.path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,file.path,new UTF8Encoding(false)); }
    var objects=files.Select(f=>f.sha256).Order(StringComparer.Ordinal).ToArray();
    var options=new JsonSerializerOptions {WriteIndented=true,NewLine="\n"}; var content=JsonSerializer.Serialize(new {group="product",objects},options)+"\n";
    var hash=ProductManifest.Hash(Encoding.UTF8.GetBytes(content)); var id="product-"+hash;
    var library="runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12";
    var manifest=new {api=2,format="board-game-builder.toolkit",version,platform="win-x64",architecture="x64",packageMode="standalone",layout="hardlinks-v1",entries=ProductManifest.RequiredEntries,
        entryTargets=ProductManifest.RequiredEntries.Where(e=>e.Key!="documentation").ToDictionary(e=>e.Value,e=>".payload/"+e.Value),
        files=files.Select(f=>new {f.path,f.size,f.sha256,f.storage,f.libraries,component=id}),
        components=new[]{new {id,kind="product",identity="product",objects,size=1,sha256=new string('a',64),source=new {repository="test/repo",tag="components-"+hash,asset=id+".zip"}}},
        libraries=new[]{new {id=library,name="runtimepack.Microsoft.NETCore.App.Runtime.win-x64",version="10.0.12"}},
        programs=paths.Where(p=>p.StartsWith(".payload/",StringComparison.Ordinal)).Select(p=>new {entry=p,runtimeConfig=p,dependencies=p,files=new[]{p},libraries=new[]{library},frameworks=new[]{new {framework="Microsoft.NETCore.App",privateVersion="10.0.12",minimumVersion="10.0.12",rollForward="LatestPatch"}}})};
    File.WriteAllText(Path.Combine(root,"toolkit.json"),JsonSerializer.Serialize(manifest,options)); return root;
}
