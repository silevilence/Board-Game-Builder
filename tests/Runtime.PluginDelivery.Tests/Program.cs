using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Rendering;
using BoardGame.Resources;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;
using GamePackage=BoardGame.AppRuntime.GamePackage;

var root=Path.GetFullPath("artifacts/external-delivery/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var contractAssemblies=new[]{typeof(IApplicationView).Assembly,typeof(IDraw2D).Assembly,typeof(AssetSet).Assembly};
var store=new PluginStore(Path.Combine(root,"installed"),"win-x64",contractAssemblies);
PluginManifest Package(ApprovedApplicationPlugin factory,Type type,string folder,params Type[] dependencies)
{
    var definition=factory.Bind(null);var directory=Path.Combine(root,folder);Directory.CreateDirectory(directory);
    var files=new List<PluginFile>();
    foreach(var assembly in dependencies.Select(t=>t.Assembly).Append(type.Assembly).Distinct())
    {
        var name=Path.GetFileName(assembly.Location);File.Copy(assembly.Location,Path.Combine(directory,name));
        files.Add(new(name,Hash(assembly.Location).ToLowerInvariant(),assembly==type.Assembly?"implementation":"managed"));
    }
    var manifest=new PluginManifest(1,"runtime",definition.Id,definition.Version,definition.Api,new(Path.GetFileName(type.Assembly.Location),type.FullName!,"Create"),["win-x64"],null,
        definition.Provides.Select(c=>new PluginCapability(c,1)).ToArray(),definition.Requires.Select(c=>new PluginCapability(c,1)).ToArray(),definition.Execution,files.ToArray())
        {ContentFields=definition.Content?.Fields};
    File.WriteAllText(Path.Combine(directory,PluginManifest.FileName),manifest.Write());store.Import(directory);return manifest;
}
var counter=Package(Example.RuntimePlugin.Factory.Create(),typeof(Example.RuntimePlugin.Factory),"counter");
var definition=Package(Example.RuntimePlugin.Document.Create(),typeof(Example.RuntimePlugin.Document),"definition");
var view=Package(Example.RuntimeView.Factory.Create(),typeof(Example.RuntimeView.Factory),"view",typeof(Example.RuntimePlugin.Counter));
RuntimePluginSelection Select(PluginManifest manifest,object configuration)=>new(manifest.Id,manifest.Version,manifest.Api,JsonSerializer.SerializeToElement(configuration),manifest.Fingerprint);
var startup=new RuntimeStartup(1,"external-sample","1",[Select(definition,new{path="state.json"}),Select(counter,new{seed=42}),Select(view,new{})]);
var project=Path.Combine(root,"project");Directory.CreateDirectory(project);
var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
File.WriteAllText(Path.Combine(project,GameRuntime.Entry),JsonSerializer.Serialize(startup,json));File.WriteAllText(Path.Combine(project,"state.json"),"{\"value\":1}");
var empty=new ApplicationCatalog([]);var expected="";
try{using var ignored=store.Open(startup with {Plugins=startup.Plugins.Select(p=>p with {Implementation=null}).ToList()},empty);throw new Exception("Unpinned external selection accepted");}catch(InvalidDataException){}
using(var lease=store.Open(startup,empty))
{
    var content=lease.Catalog.Load(project);expected=content.Fingerprint;
    await Run(lease.Catalog,content);
    try{store.Remove(counter.Id,counter.Version);throw new Exception("Active selected version removed");}catch(InvalidOperationException){}
    var other=new PluginStore(store.DirectoryPath,"win-x64",contractAssemblies);
    try{other.Remove(counter.Id,counter.Version);throw new Exception("Another installation handle removed an active version");}catch(IOException){}
    Check(other.Installed().Count==3,"Failed removal damaged installation");
    try{other.Import(Path.Combine(root,"counter"),true);throw new Exception("Another installation handle replaced an active version");}catch(IOException){}
    Check(other.Installed().Count==3,"Failed replacement damaged installation");
    GamePackage.Pack(project,Path.Combine(root,"data-only.bgm"),lease.Catalog.Content);
    using var zip=System.IO.Compression.ZipFile.OpenRead(Path.Combine(root,"data-only.bgm"));
    Check(!zip.Entries.Any(e=>e.FullName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)),"Data package contains managed implementations");
    try{GamePackage.Load(Path.Combine(root,"data-only.bgm"),empty.Content);throw new Exception("Package auto-loaded unapproved implementations");}catch(InvalidDataException){}
}
var author=new ApplicationAuthorRuntime("external",empty,store,true);
var snapshot=new ProjectView(new(project,"external","memory"),Directory.GetFiles(project).ToDictionary(p=>Path.GetFileName(p),File.ReadAllBytes));
Check(author.Supports(snapshot),"Imported definition not discoverable");await author.Validate(snapshot,"runtime");
var previousStore=Environment.GetEnvironmentVariable("BGB_RUNTIME_PLUGIN_STORE");
try
{
    Environment.SetEnvironmentVariable("BGB_RUNTIME_PLUGIN_STORE",store.DirectoryPath);
    await using var host=await AuthorHost.Start(new("external","test",project,new(1,[new("edit.disk"),new("author.runtime.external"),new("author.run.application")])));
    var batch=JsonSerializer.SerializeToElement(await host.Invoke("runtime.run",JsonSerializer.SerializeToElement(new{ticks=12,count=3}),"external-batch"));
    Check(batch.GetProperty("Fingerprint").GetString()==expected && batch.GetProperty("runs").GetArrayLength()==3 &&
        batch.GetProperty("runs").EnumerateArray().All(r=>r.GetProperty("ticks").GetInt64()==12),"Batch selected identity or fixed updates differ");
    File.WriteAllText(Path.Combine(root,"batch.json"),batch.GetRawText());
}
finally{Environment.SetEnvironmentVariable("BGB_RUNTIME_PLUGIN_STORE",previousStore);}
if(args.FirstOrDefault() is {} release)
{
    var destination=Path.Combine(root,"standalone");
    author.PreflightPlayer(release,snapshot,default);
    var actual=author.ExportPlayer(release,snapshot,destination,new(snapshot,new(),JsonSerializer.SerializeToElement(new{})));
    Check(actual==expected,"Ordinary export changed runtime identity");
    foreach(var manifest in new[]{counter,definition,view})store.Remove(manifest.Id,manifest.Version);
    var detached=new PluginStore(PluginPlayerDistribution.InstalledDirectory(destination),"win-x64",contractAssemblies);
    using(var lease=detached.Open(startup,empty))
    {
        var content=lease.Catalog.Load(Path.Combine(destination,"game.bgm"));Check(content.Fingerprint==expected,"Detached game identity changed");await Run(lease.Catalog,content);
    }
    var binary=Directory.GetFiles(PluginPlayerDistribution.InstalledDirectory(destination),"Runtime.SampleView.dll",SearchOption.AllDirectories).Single();var original=File.ReadAllBytes(binary);
    File.AppendAllText(binary,"tamper");
    try{using var ignored=detached.Open(startup,empty);throw new Exception("Tampered external implementation accepted");}catch(InvalidDataException){}
    File.WriteAllBytes(binary,original);
    var missing=binary+".missing";File.Move(binary,missing);
    try{using var ignored=detached.Open(startup,empty);throw new Exception("Missing external implementation accepted");}catch(InvalidDataException){}
    finally{File.Move(missing,binary);}
    File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{success=true,sourceRemoved=true,fingerprint=expected,ticks=12,destination},json));
    Console.WriteLine("PASS standalone plugin delivery, source installation removal, exact content identity and tamper rejection: "+destination);
}
Console.WriteLine("PASS grouped ordinary plugin catalog: shared dependency types, selected identity, fixed updates, lease protection, author validation and data-only package: "+root);
static async Task Run(ApplicationCatalog catalog,GameContent content)
{
    await using var host=await catalog.StartAsync(content,clock:new(TimeSpan.FromMilliseconds(20)));host.Driver.Paused=true;
    for(var i=0;i<12;i++)await host.Driver.StepAsync();
    Check(host.Service<IApplicationView>(ApplicationViewPlugin.Capability).Description.Title=="External runtime sample","External public contract type differs");
    var counter=host.Service<object>("sample.counter");Check((int)counter.GetType().GetProperty("Ticks")!.GetValue(counter)! == 12,"Selected external updates differ");
    uint expected=42;for(var i=0;i<12;i++)expected=unchecked(expected*1664525+1013904223);
    Check((uint)counter.GetType().GetProperty("Value")!.GetValue(counter)! == expected,"Seeded external result differs");
}
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void Check(bool value,string message){if(!value)throw new Exception(message);}
