using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Authoring;
using Sample.Motion;

var sdk=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--",StringComparison.Ordinal)) ?? "artifacts/native-aot/sdk-motion-02");
var output=Path.GetFullPath("artifacts/native-aot/author/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
var source=Path.GetFullPath("samples/motion-lab");
var files=Directory.GetFiles(source).ToDictionary(p=>Path.GetFileName(p),p=>File.ReadAllBytes(p));
var original=new ProjectView(new(source,"native-test","memory"),files);
var startup=JsonNode.Parse(files[GameRuntime.Entry]!)!.AsObject();
startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="render.desktopgl",["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
files[GameRuntime.Entry]=Encoding.UTF8.GetBytes(startup.ToJsonString());
var motion=JsonNode.Parse(files["motion.json"]!)!.AsObject();motion["title"]="未保存原生游戏";files["motion.json"]=Encoding.UTF8.GetBytes(motion.ToJsonString());
var view=new ProjectView(new(source,"native-test","memory",Generation:3),files!);
var runtimes=new AuthorRuntimes();using var runtime=runtimes.Register(new ApplicationAuthorRuntime("motion",new ApplicationCatalog(MotionPlugins.Catalog())));
var registry=new AuthorExports();using var package=registry.Register(new PackageExporter());using var player=registry.Register(new PlayerExporter());
var native=registry.Register(new NativeExporter());
JsonElement Arguments(string name,string? dotnet=null)=>JsonSerializer.SerializeToElement(new Dictionary<string,string>{["target"]=Path.Combine(output,name),["sdk"]=sdk,["title"]="未保存原生游戏",["dotnet"]=dotnet ?? "dotnet"});
var request=new AuthorExportRequest(view,runtimes,Arguments("game"));
Check(registry.Items.Count==3 && registry.Items.Select(p=>p.Plugin).Distinct().Count()==3,"Export providers are not independent");
var missingBackend=await registry.Preflight("native-directory",request with {View=original});
Check(!missingBackend.Available && missingBackend.Diagnostics.Any(d=>d.Message.Contains("exactly one render.backend")),"Missing backend silently replaced");
var missingTools=await registry.Preflight("native-directory",request with {Arguments=Arguments("missing-tools",Path.Combine(output,"missing-dotnet.exe"))});
Check(!missingTools.Available && missingTools.Diagnostics.Any(d=>d.Code=="toolchain.sdk"),"Missing compiler accepted");
var ready=await registry.Preflight("native-directory",request);Check(ready.Available,string.Join("; ",ready.Diagnostics.Select(d=>d.Message)));
var pinned=JsonNode.Parse(files[GameRuntime.Entry]!)!.AsObject();
pinned["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="render.desktopgl")!["implementation"]=BoardGame.Publishing.RuntimeSdk.Open(sdk).Plugins.Single(p=>p.Input.Manifest.Id=="render.desktopgl").Input.Manifest.Fingerprint;
var pinnedFiles=files.ToDictionary(p=>p.Key,p=>p.Value);pinnedFiles[GameRuntime.Entry]=Encoding.UTF8.GetBytes(pinned.ToJsonString());
var pinnedCheck=await registry.Preflight("native-directory",request with {View=new ProjectView(view.Source.Identity,pinnedFiles!)});
Check(pinnedCheck.Available,"Pinned SDK graphics identity was replaced by inactive host metadata");
if(args.Contains("--native"))
{
    using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(3));var canceledAtCompile=false;
    var progress=new InlineProgress(line=>
    {
        if(line.Contains("Generating native code",StringComparison.Ordinal))
        {
            Check(registry.Active("native-directory"),"Build did not retain its provider");
            try{native.Dispose();throw new Exception("Active provider removed");}catch(InvalidOperationException error){Check(error.Message.Contains("export.active"),"Unexpected lifetime diagnostic");}
            canceledAtCompile=true;cancellation.Cancel();
        }
    });
    try{await registry.Export("native-directory",request with {Arguments=Arguments("cancelled"),Cancellation=cancellation.Token,Progress=progress});throw new Exception("Cancelled publication succeeded");}
    catch(OperationCanceledException){}
    Check(canceledAtCompile && !Directory.Exists(Path.Combine(output,"cancelled")) && !registry.Active("native-directory"),"Cancelled provider leaked output/lifetime");
    var artifact=await registry.Export("native-directory",request);
    Check(artifact.Source==view.Source && artifact.Kind=="native-directory" && File.Exists(artifact.Log),"Native result lost source/log identity");
    Check(JsonNode.Parse(File.ReadAllText(Path.Combine(artifact.Target,"content/motion.json")))!["title"]!.GetValue<string>()=="未保存原生游戏","Publication did not use the captured draft");
    File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(artifact,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("PASS native author build: active provider protection, compiler cancellation, retry, snapshot publication and result identity: "+output);
}
native.Dispose();Check(registry.Items.Count==2,"Disabling native provider affected other providers");
var packaged=await registry.Export("game-package",request with {View=original,Arguments=JsonSerializer.SerializeToElement(new{target=Path.Combine(output,"ordinary.bgm")})});
Check(File.Exists(packaged.Target),"Ordinary package export failed without native provider");
using var again=registry.Register(new NativeExporter());Check(registry.Items.Count==3,"Native provider cannot be re-enabled");
Check(File.ReadAllBytes(Path.Combine(source,"motion.json")).SequenceEqual(original.Bytes("motion.json")) && !File.ReadAllText(Path.Combine(source,GameRuntime.Entry)).Contains("render.desktopgl"),"Source project was saved or modified");
Console.WriteLine("PASS independent native export provider: three providers, unavailable toolchain, explicit backend, optional activation and untouched source");
static void Check(bool value,string message){if(!value)throw new Exception(message);}
sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string value)=>report(value);}
