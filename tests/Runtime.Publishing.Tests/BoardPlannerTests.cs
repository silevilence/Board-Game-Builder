using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Publishing;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;

internal static class BoardPlannerTests
{
    internal static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"board-publication-plan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var common=Assemblies(typeof(PluginRegistration).Assembly);
            var shared=common.Values.Select(a=>new PublicationFile(a.Location,Path.GetFileName(a.Location),Hash(a.Location),"managed","core")).ToArray();
            var board=new BoardApplication();var map=board.Plugins().ToDictionary(p=>p.Bind(null).Id,StringComparer.Ordinal);
            var methods=new Dictionary<string,string>{["definition.board-state"]="Definition",["resources"]="Resources",["kernel"]="Authority",["rules.commands"]="Rules",["recording"]="Recording",["session.local"]="Session"};
            var factories=methods.Select(p=>(Factory:map[p.Key],Type:typeof(BoardStaticFactories),Method:p.Value)).Append((Factory:WasmStaticFactories.Create(),Type:typeof(WasmStaticFactories),Method:"Create")).ToArray();
            var available=new List<AvailablePlugin>();var pinned=new List<ApprovedApplicationPlugin>();
            foreach(var factory in factories)
            {
                var description=factory.Factory.Bind(null);var folder=Path.Combine(directory,description.Id);Directory.CreateDirectory(folder);
                var files=new List<PluginFile>();
                foreach(var assembly in Assemblies(factory.Type.Assembly).Values.Where(a=>!common.ContainsKey(a.GetName().Name!)))
                {
                    var name=Path.GetFileName(assembly.Location);File.Copy(assembly.Location,Path.Combine(folder,name));
                    files.Add(new(name,Hash(assembly.Location),assembly==factory.Type.Assembly?"implementation":"managed"));
                }
                var support=new PluginAot(true,["win-x64"],"Formal board chain verified separately");
                if(description.Id=="wasm")
                {
                    support=BuiltinScriptSupport.Declaration("wasm","1",1);
                    var native=Path.Combine(AppContext.BaseDirectory,"runtimes/win-x64/native/wasmtime.dll");
                    if(!File.Exists(native))native=Path.Combine(AppContext.BaseDirectory,"wasmtime.dll");
                    File.Copy(native,Path.Combine(folder,"wasmtime.dll"));files.AddRange(BuiltinScriptSupport.NativeFiles("wasm","1",1,"win-x64",folder));
                }
                var manifest=new PluginManifest(1,"runtime",description.Id,description.Version,description.Api,
                    new(Path.GetFileName(factory.Type.Assembly.Location),factory.Type.FullName!,factory.Method),["win-x64"],support,
                    description.Provides.Select(p=>new PluginCapability(p,1)).ToArray(),description.Requires.Select(p=>new PluginCapability(p,1)).ToArray(),description.Execution,files.ToArray());
                available.Add(new(new(manifest,folder),true));pinned.Add(PluginRegistration.Bind(manifest,factory.Factory));
            }
            var catalog=new ApplicationCatalog(factories.Select(f=>f.Factory));
            var pinnedCatalog=new ApplicationCatalog(pinned);
            RuntimePluginSelection P(string id,string config="{}") {using var json=JsonDocument.Parse(config);return new(id,"1",1,json.RootElement.Clone());}
            var startup=new RuntimeStartup(1,"board-plan","1",[P("definition.board-state","""{"path":"state.json"}"""),P("resources"),P("kernel"),P("session.local"),P("recording"),P("rules.commands")]);
            var state=new GameState{Seats=[new("p1","one")],Phases=["play"],Phase="play"};
            var source=new Dictionary<string,byte[]>{[GameRuntime.Entry]=Encoding.UTF8.GetBytes(Wire.Json(startup)),["state.json"]=Encoding.UTF8.GetBytes(Wire.Json(new{format=1,state}))};
            var noScripts=catalog.LoadFilesForTest(source);
            var simple=PublicationPlanner.Analyze(noScripts,available,[],shared,"win-x64",HostMode.Headless);
            Check(!simple.Files.Any(f=>f.Name.Contains("Wasm",StringComparison.OrdinalIgnoreCase)),"Unselected WASM implementation or native library included");
            Check(simple.Selected.Count==6,"Minimal board plugin set changed");
            var binary=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"counter.wasm"));
            startup.Plugins.Add(P("wasm",JsonSerializer.Serialize(new{extensions=new[]{new{id="counter",version="1",api=1,kind="wasm",file="counter.wasm",sha256=Convert.ToHexString(SHA256.HashData(binary)),permissions=new[]{"component","command"}}}})));
            source[GameRuntime.Entry]=Encoding.UTF8.GetBytes(Wire.Json(startup));source["counter.wasm"]=binary;
            var scripted=catalog.LoadFilesForTest(source);
            var wasm=PublicationPlanner.Analyze(scripted,available,[],shared,"win-x64",HostMode.Headless);
            Check(wasm.Files.Any(f=>f.Name=="wasmtime.dll" && f.Role=="native") && wasm.Files.Any(f=>f.Name=="Wasm.Execution.dll"),"WASM closure incomplete");
            Check(!wasm.Files.Any(f=>f.Name.Contains("Lua") || f.Name.Contains("YamlDotNet")),"WASM contains unselected executor/author parser");
            var prepared=PublicationInputs.Create(wasm,Path.Combine(directory,"prepared")).GetAwaiter().GetResult();
            var pinnedContent=pinnedCatalog.Load(Path.Combine(prepared.Directory,"content"));
            Check(pinnedContent.Identity.Startup.Plugins.All(p=>p.Implementation!=null),"Release identity not pinned");
            Check(scripted.Identity.Startup.Plugins.All(p=>p.Implementation==null),"Source snapshot mutated");
            Check(prepared.ContentFiles.Contains("counter.wasm"),"Guest content missing");
            Console.WriteLine("PASS actual no-script/WASM board plans, full native/managed closure, script exclusion and isolated release identity pinning");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static GameContent LoadFilesForTest(this ApplicationCatalog catalog,IReadOnlyDictionary<string,byte[]> files)=>GameRuntime.LoadFiles(files,catalog.Content);
    private static Dictionary<string,Assembly> Assemblies(Assembly root)
    {
        var result=new Dictionary<string,Assembly>(StringComparer.Ordinal);
        void Visit(Assembly assembly)
        {
            if(Path.GetDirectoryName(assembly.Location)==Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()))return;
            if(!result.TryAdd(assembly.GetName().Name!,assembly))return;
            foreach(var reference in assembly.GetReferencedAssemblies())Visit(Assembly.Load(reference));
        }
        Visit(root);return result;
    }
    private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
