using BoardGame.Plugins;
using BoardGame.Runtime;

var checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
void Reject(Action action,string message,string? diagnostic=null)
{try{action();}catch(InvalidDataException error) when(diagnostic==null || error.Message.Contains(diagnostic,StringComparison.OrdinalIgnoreCase)){checks++;return;}throw new Exception(message);}
PluginManifest Manifest(string id)=>new(1,"runtime",id,"1",1,new("Plugin.dll","Example.Factory","Create"),["win-x64"],
    new(true,["win-x64"],"Declaration only; requires native verification"),[new(id,1)],[],new(),[new("Plugin.dll",new('a',64),"implementation")]);
var fixture=Path.GetFullPath("samples/Runtime.SamplePlugin/bin/Release/net10.0/Runtime.SamplePlugin.dll");
var workspace=Path.GetFullPath(".scratch/runtime-plugins/"+Guid.NewGuid().ToString("N"));
var package=Path.Combine(workspace,"package");Directory.CreateDirectory(package);
File.Copy(fixture,Path.Combine(package,"Runtime.SamplePlugin.dll"));
var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture)));
var external=Manifest("sample.counter") with {Entry=new("Runtime.SamplePlugin.dll","Example.RuntimePlugin.Factory","Create"),
    Execution=new(HostMode.All,RuntimeScope.Instance,[new("tick",ApplicationPhase.Update)]),Files=[new("Runtime.SamplePlugin.dll",hash,"implementation")]};
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),external.Write());
var store=new PluginStore(Path.Combine(workspace,"installed"),"win-x64");
using(var cancellation=new CancellationTokenSource())
{
    cancellation.Cancel();
    try{store.Import(package,cancellation:cancellation.Token);throw new Exception("Cancelled import installed a package");}catch(OperationCanceledException){checks++;}
    Check(store.Installed().Count==0,"Cancelled import changed installation");
}
using(var fixedCatalog=store.RegisterBuiltins("probe",[new("sample.counter","1",1,["sample.counter"],[],_=>{})]))
    Reject(()=>store.Import(package),"Registered fixed implementation overwritten","built-in");
store.Import(package);Check(store.Installed().Count==1,"Import did not register implementation");
var generated=StaticRegistration.Generate([new(external,package)],"win-x64",HostMode.Headless);
Check(generated.References.Count==1 && generated.NativeFiles.Count==0 && generated.Source.Contains("global::@Example.@RuntimePlugin.@Factory.@Create()",StringComparison.Ordinal),"Static selection differs from imported factory");
Check(!generated.Source.Contains("LoadFrom",StringComparison.Ordinal),"Generated registration dynamically loads implementation");
Reject(()=>store.Import(package),"Duplicate import allowed","already installed");
Reject(()=>new PluginStore(Path.Combine(workspace,"builtin-conflict"),"win-x64",builtinManifests:[external]).Import(package),"Built-in identity overwritten","built-in");
Reject(()=>new PluginStore(Path.Combine(workspace,"wrong-platform"),"linux-x64").Import(package),"Wrong platform accepted","unsupported target");
using(var lease=store.Acquire(external.Id,external.Version))
{
    var plugin=lease.Plugin.Bind(null);
    using var configuration=System.Text.Json.JsonDocument.Parse("{}");
    var startup=new BoardGame.Kernel.RuntimeStartup(1,"external-probe","1",[new(plugin.Id,plugin.Version,1,configuration.RootElement.Clone(),plugin.Implementation)]);
    Reject(()=>RuntimeHost.Validate(startup with {Plugins=[startup.Plugins[0] with {Implementation=null}]},[plugin],[]),"External hash omitted");
    await using var host=await RuntimeHost.StartAsync(startup,[plugin],[]);
    await host.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(20));
    var counter=host.Service<object>("sample.counter");
    Check((int)counter.GetType().GetProperty("Ticks")!.GetValue(counter)! == 1,"Imported update did not run");
    try{store.Remove(external.Id,external.Version);throw new Exception("Active implementation removed");}catch(InvalidOperationException){checks++;}
    try{store.Import(package,true);throw new Exception("Active implementation replaced");}catch(InvalidOperationException){checks++;}
}
store.Remove(external.Id,external.Version);Check(store.Installed().Count==0,"Removed implementation still installed");
var fixedService=new object();var hostPlugin=new RuntimePluginDefinition("sample.host","1",1,["sample.host"],[],s=>s.Provide("sample.host",fixedService));
var consumer=external with {Id="sample.consumer",Entry=external.Entry with {Type="Example.RuntimePlugin.HostServiceConsumer"},Provides=[new("sample.consumer",1)],Requires=[new("sample.host",1)],Execution=new()};
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),consumer.Write());
using(var fixedCatalog=store.RegisterBuiltins("host",[hostPlugin]))
{
    store.Import(package);using var lease=store.Acquire(consumer.Id,consumer.Version);var plugin=lease.Plugin.Bind(null);
    var configuration=System.Text.Json.JsonSerializer.SerializeToElement(new{});
    await using var host=await RuntimeHost.StartAsync(new(1,"consumer","1",[new(hostPlugin.Id,"1",1,configuration),new(plugin.Id,"1",1,configuration,plugin.Implementation)]),[hostPlugin,plugin],[]);
    Check(ReferenceEquals(host.Service<object>("sample.consumer"),fixedService),"Imported plugin did not consume fixed host contract");
}
store.Remove(consumer.Id,consumer.Version);
var badEntry=external with {Entry=external.Entry with {Method="Missing"}};
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),badEntry.Write());
Reject(()=>store.Import(package),"Missing entry accepted","factory must be");
Check(!Directory.EnumerateFileSystemEntries(Path.Combine(workspace,"installed")).Any(),"Failed factory import left staging files");
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),(external with {Id="different"}).Write());
Reject(()=>store.Import(package),"Factory identity mismatch accepted","factory differs");
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),(external with {Requires=[new("absent",1)]}).Write());
Reject(()=>store.Import(package),"Missing capability accepted","missing compatible installed capability");
File.WriteAllText(Path.Combine(package,PluginManifest.FileName),external.Write());
File.AppendAllText(Path.Combine(package,"Runtime.SamplePlugin.dll"),"tamper");
Reject(()=>store.Import(package),"Tampered assembly imported","hash mismatch");Check(store.Installed().Count==0,"Failed import polluted catalogue");
Reject(()=>StaticRegistration.Generate([new(external,package)],"win-x64",HostMode.Headless),"Changed static implementation accepted");
foreach(var sample in new[]{"Editor.SamplePlugin","Sample.Motion"})
{
    var candidate=Path.Combine(workspace,sample);Directory.CreateDirectory(candidate);
    var assembly=Directory.GetFiles(Path.GetFullPath("samples/"+sample+"/bin/Release"),sample+".dll",SearchOption.AllDirectories).First();
    File.Copy(assembly,Path.Combine(candidate,sample+".dll"));
    var fileHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly)));
    var descriptor=external with {Entry=external.Entry with {Assembly=sample+".dll"},Files=[new(sample+".dll",fileHash,"implementation")]};
    File.WriteAllText(Path.Combine(candidate,PluginManifest.FileName),descriptor.Write());
    Reject(()=>store.Import(candidate),sample+": editor/missing dependency import accepted",sample=="Editor.SamplePlugin"?"Editor-specific":"managed dependency");
    Check(!Directory.EnumerateFileSystemEntries(Path.Combine(workspace,"installed")).Any(),"Rejected package left installed files");
}
Console.WriteLine($"PASS {checks} including real external factory, update, identity, tamper and active-version protection");
