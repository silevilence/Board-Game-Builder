using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Player;
using BoardGame.Plugins;
using BoardGame.Publishing;
using BoardGame.Rendering;
using BoardGame.Resources;
using Sample.Motion;

// Maintainer tool: produces a precompiled distribution. It is never shipped in a game.
if(args.Length<2)throw new ArgumentException("Usage: NativeSdk pack <new-sdk-directory> | motion <sdk-directory> <game-directory> <new-output-directory> [icon.ico]");
if(args[0]=="pack")
{
    var destination=Path.GetFullPath(args[1]);
    if(Directory.Exists(destination) || File.Exists(destination))throw new IOException("SDK destination already exists");
    Directory.CreateDirectory(destination);
    try
    {
        var common=Closure(typeof(NativeApplicationEntry).Assembly,typeof(PluginRegistration).Assembly);
        var hostFiles=common.Values.Select(a=>Copy(a.Location,destination,"hosts/application", "managed")).ToList();
        foreach(var name in new[]{"av_libglesv2.dll","libHarfBuzzSharp.dll","libSkiaSharp.dll"})hostFiles.Add(Copy(Native(name),destination,"hosts/application","native"));
        var packages=new List<string>();
        void Add(Type type,string method,params string[] native)
        {
            var factory=(ApprovedApplicationPlugin)(type.GetMethod(method,BindingFlags.Public|BindingFlags.Static)!.Invoke(null,null)!);
            var definition=factory.Bind(null);var folder="plugins/"+definition.Id;
            var files=Closure(type.Assembly).Values.Where(a=>!common.ContainsKey(a.GetName().Name!)).Select(a=>
                Copy(a.Location,Path.Combine(destination,folder),"",a==type.Assembly?"implementation":"managed")).ToList();
            // An entry may live in the host closure; each package still carries its declared implementation.
            if(!files.Any(f=>f.Role=="implementation"))files.Add(Copy(type.Assembly.Location,Path.Combine(destination,folder),"","implementation"));
            foreach(var name in native)files.Add(Copy(Native(name),Path.Combine(destination,folder),"","native"));
            var manifest=new PluginManifest(1,"runtime",definition.Id,definition.Version,definition.Api,
                new(Path.GetFileName(type.Assembly.Location),type.FullName!,method),["win-x64"],new(true,["win-x64"],"Fixed runtime distribution; strict compilation and release validation required"),
                definition.Provides.Select(c=>new PluginCapability(c,1)).ToArray(),definition.Requires.Select(c=>new PluginCapability(c,1)).ToArray(),definition.Execution,files.ToArray())
                {ContentFields=definition.Content?.Fields.ToArray()};
            File.WriteAllText(Path.Combine(destination,folder,PluginManifest.FileName),manifest.Write());packages.Add(folder);
        }
        Add(typeof(MotionPlugins),nameof(MotionPlugins.Definition));Add(typeof(MotionPlugins),nameof(MotionPlugins.Loop));Add(typeof(MotionPlugins),nameof(MotionPlugins.ViewFactory));
        Add(typeof(AssetStaticFactories),nameof(AssetStaticFactories.Resources));Add(typeof(AssetStaticFactories),nameof(AssetStaticFactories.Images));
        Add(typeof(AssetStaticFactories),nameof(AssetStaticFactories.TexturePacker));Add(typeof(AssetStaticFactories),nameof(AssetStaticFactories.Font));
        Add(typeof(DesktopGlBackend),nameof(DesktopGlBackend.Create),"SDL2.dll");
        RuntimeSdk.WriteManifest(destination,new(1,"win-x64",[new("application.desktop",typeof(NativeApplicationEntry).FullName!,"Run",[],[ApplicationViewPlugin.Capability],hostFiles.ToArray())],packages.ToArray()));
        _=RuntimeSdk.Open(destination);Console.WriteLine("SDK: "+destination);
    }
    catch {Directory.Delete(destination,true);throw;}
}
else if(args[0]=="motion" && args.Length is 4 or 5)
{
    var sdk=RuntimeSdk.Open(args[1]);var catalog=new ApplicationCatalog(MotionPlugins.Catalog());var content=catalog.Load(args[2]);
    using var empty=JsonDocument.Parse("{}");
    var plan=sdk.Plan(content,"application.desktop",[new RuntimePluginSelection("render.desktopgl","1",1,empty.RootElement.Clone())]);
    var title=content.Payload.Data.GetProperty("settings").GetProperty("title").GetString()!;
    var result=await NativeBuildService.Build(plan,sdk.Entry("application.desktop",title,content.Identity.Startup.Version,args.Length==5?File.ReadAllBytes(args[4]):null),new(args[3]),new Progress<NativeBuildProgress>(p=>Console.WriteLine(p.Stage+": "+p.Message)));
    Console.WriteLine(JsonSerializer.Serialize(result));return result.Success?0:1;
}
else throw new ArgumentException("Unknown command");
return 0;

static Dictionary<string,Assembly> Closure(params Assembly[] roots)
{
    var result=new Dictionary<string,Assembly>(StringComparer.Ordinal);
    void Visit(Assembly assembly)
    {
        if(string.Equals(Path.GetDirectoryName(assembly.Location),Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()),StringComparison.OrdinalIgnoreCase))return;
        if(!result.TryAdd(assembly.GetName().Name!,assembly))return;
        foreach(var reference in assembly.GetReferencedAssemblies())Visit(Assembly.Load(reference));
    }
    foreach(var root in roots)Visit(root);return result;
}
static PluginFile Copy(string source,string destination,string folder,string role)
{
    var relative=(folder.Length>0?folder+"/":"")+Path.GetFileName(source);var path=Path.Combine(destination,relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(source,path);
    using var stream=File.OpenRead(path);return new(relative,Convert.ToHexStringLower(SHA256.HashData(stream)),role);
}
static string Native(string name)
{
    var path=Path.Combine(AppContext.BaseDirectory,name);if(File.Exists(path))return path;
    path=Path.Combine(AppContext.BaseDirectory,"runtimes/win-x64/native",name);
    return File.Exists(path)?path:throw new FileNotFoundException("Missing native SDK dependency",path);
}
