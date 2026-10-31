using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Publishing;
using BoardGame.Runtime;

internal static class PlannerTests
{
    internal static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"publication-plan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=typeof(Example.RuntimePlugin.Factory).Assembly.Location;var name=Path.GetFileName(path);
            File.Copy(path,Path.Combine(directory,name));
            PluginManifest Manifest(ApprovedApplicationPlugin factory,string type,string method="Create")
            {
                var definition=factory.Bind(null);
                return new(1,"runtime",definition.Id,"1",1,new(name,type,method),["win-x64"],new(true,["win-x64"],"Test candidate; compilation remains mandatory"),
                    definition.Provides.Select(p=>new PluginCapability(p,1)).ToArray(),definition.Requires.Select(p=>new PluginCapability(p,1)).ToArray(),definition.Execution,
                    [new(name,Hash(path),"implementation")]);
            }
            var definition=Manifest(Example.RuntimePlugin.Document.Create(),"Example.RuntimePlugin.Document");
            var counter=Manifest(Example.RuntimePlugin.Factory.Create(),"Example.RuntimePlugin.Factory");
            var catalog=new ApplicationCatalog([PluginRegistration.Bind(definition,Example.RuntimePlugin.Document.Create()),PluginRegistration.Bind(counter,Example.RuntimePlugin.Factory.Create())]);
            var startup=new RuntimeStartup(1,"test","1",[P(definition,"""{"path":"default.json","presets":{"first":"default.json","second":"other.json"}}"""),P(counter)]);
            var content=GameRuntime.LoadFiles(new Dictionary<string,byte[]>{[GameRuntime.Entry]=JsonSerializer.SerializeToUtf8Bytes(startup,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase}),["default.json"]=Encoding.UTF8.GetBytes("{\"value\":1}"),["other.json"]=Encoding.UTF8.GetBytes("{\"value\":2}")},catalog.Content);
            AvailablePlugin[] available=[new(new(definition,directory)),new(new(counter,directory))];
            var shared=new Dictionary<string,PublicationFile>(StringComparer.Ordinal);
            void Include(Assembly assembly)
            {
                var identity=assembly.GetName().Name!;
                if(Path.GetDirectoryName(assembly.Location)==Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()))return;
                if(!shared.TryAdd(identity,new(assembly.Location,Path.GetFileName(assembly.Location),Hash(assembly.Location),"managed","shared")))return;
                foreach(var reference in assembly.GetReferencedAssemblies())Include(Assembly.Load(reference));
            }
            Include(typeof(ApplicationCatalog).Assembly);Include(typeof(PluginRegistration).Assembly);
            PublicationPlan Plan(AvailablePlugin[]? source=null,RuntimePluginSelection[]? host=null,HostMode mode=HostMode.Headless)=>PublicationPlanner.Analyze(content,source ?? available,host ?? [],shared.Values,"win-x64",mode);
            var plan=Plan();Check(plan.Content.ExportSnapshot().Files.ContainsKey("other.json"),"Unplayed preset dropped");
            var prepared=PublicationInputs.Create(plan,Path.Combine(directory,"inputs")).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(prepared.Directory,"content/other.json")),"Prepared preset missing");
            Check(!prepared.ProjectItems.Contains(directory,StringComparison.Ordinal),"Prepared references still depend on installation paths");
            Check(GameRuntime.Load(Path.Combine(prepared.Directory,"content"),catalog.Content).Fingerprint==content.Fingerprint,"Pinned external content identity changed");
            var canceled=Path.Combine(directory,"canceled");
            try{PublicationInputs.Create(plan,canceled,new CancellationToken(true)).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
            Check(!Directory.Exists(canceled),"Canceled input directory survived");
            Check(plan.Selected.Count==2 && plan.Registration.Source.Contains("Example.@RuntimePlugin.@Document"),"Static factory missing");
            Check(!plan.Files.Any(f=>f.Name.StartsWith("BoardGame.",StringComparison.Ordinal) || f.Name.Contains("Lua") || f.Name.Contains("Wasm") || f.Name.Contains("Editor")),"Optional implementation leaked into minimal plan");
            Reject(()=>Plan([available[0]]),"missing");
            Reject(()=>Plan([available[0],new(new(counter with {Version="2"},directory))]),"missing");
            // A declaration from the content must match the implementation bytes/metadata, not just its version.
            Reject(()=>Plan([available[0],new(new(counter with {Aot=new(false,[],"incompatible")},directory))]),"identity mismatch");
            Reject(()=>Plan(host:[P(counter)]),"duplicate");
            Reject(()=>Plan(mode:HostMode.Graphical),"exactly one");
            var backend=counter with {Id="test.backend",Provides=[new("render.backend",1)],Execution=new(HostMode.Graphical,RuntimeScope.GraphicsHost),Aot=new(false,[],"ordinary only")};
            var extra=available.Append(new AvailablePlugin(new(backend,directory))).ToArray();
            var headless=Plan(extra,[P(backend)]);
            Check(headless.Registration.Source.Contains("PluginRegistration.IdentityOnly"),"Headless did not preserve dormant backend identity");
            var dormant=PluginRegistration.IdentityOnly(backend).Bind(null);
            Check(dormant.Implementation==backend.Fingerprint,"Dormant identity changed");
            var dormantCatalog=new ApplicationCatalog([PluginRegistration.Bind(definition,Example.RuntimePlugin.Document.Create()),PluginRegistration.Bind(counter,Example.RuntimePlugin.Factory.Create()),PluginRegistration.IdentityOnly(backend)]);
            var dormantStartup=startup with {Plugins=[..startup.Plugins,P(backend)]};
            var dormantFiles=content.ExportSnapshot().Files;
            dormantFiles[GameRuntime.Entry]=JsonSerializer.SerializeToUtf8Bytes(dormantStartup,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
            var dormantContent=GameRuntime.LoadFiles(dormantFiles,dormantCatalog.Content);
            var dormantPlan=PublicationPlanner.Analyze(dormantContent,extra,[],shared.Values,"win-x64",HostMode.Headless);
            Check(dormantPlan.Registration.Source.Contains("IdentityOnly"),"Game-declared dormant graphics identity was lost");
            Reject(()=>Plan(extra,[P(backend)],HostMode.Graphical),"ordinary only");
            var supported=backend with {Aot=new(true,["win-x64"],"test")};
            var second=supported with {Id="test.other-backend"};
            Reject(()=>Plan([..available,new(new(supported,directory)),new(new(second,directory))],[P(supported),P(second)],HostMode.Graphical),"Multiple providers");
            var absent=shared.Values.Where(f=>f.Name!="CordiSharp.dll").ToArray();
            Reject(()=>PublicationPlanner.Analyze(content,available,[],absent,"win-x64",HostMode.Headless),"CordiSharp");
            File.AppendAllText(Path.Combine(directory,name),"tamper");Reject(()=>Plan(),"content changed");
            Console.WriteLine("PASS publication plan: complete presets, exact identity, static references, optional omission, headless graphics, backend conflicts, managed closure and tamper rejection");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static RuntimePluginSelection P(PluginManifest manifest,string config="{}")
    {using var json=JsonDocument.Parse(config);return new(manifest.Id,manifest.Version,manifest.Api,json.RootElement.Clone(),manifest.Fingerprint);}
    private static string Hash(string file)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Reject(Action action,string text)
    {try{action();}catch(InvalidDataException error)when(error.Message.Contains(text,StringComparison.OrdinalIgnoreCase)){return;}throw new Exception("Expected rejection: "+text);}
}
