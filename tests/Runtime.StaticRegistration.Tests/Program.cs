using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Runtime;

var repo=Path.GetFullPath(".");
var workspace=Path.Combine(repo,".scratch/static-registration-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
var board=new BoardApplication();
var ordinary=board.Plugins().ToDictionary(p=>p.Bind(null).Id,StringComparer.Ordinal);
var entries=new Dictionary<string,string>{["definition.board-state"]="Definition",["resources"]="Resources",["kernel"]="Authority",
    ["rules.commands"]="Rules",["recording"]="Recording",["session.local"]="Session"};
await Verify("board",entries.Select(p=>(Factory:ordinary[p.Key],Type:typeof(BoardStaticFactories),Method:p.Value)).ToArray());
await Verify("nonboard",[(Example.RuntimePlugin.Factory.Create(),typeof(Example.RuntimePlugin.Factory),"Create")]);
Console.WriteLine("PASS actual generated board/nonboard projects: identity, capabilities, scopes, activation/phase order and selected references");

async Task Verify(string name,(ApprovedApplicationPlugin Factory,Type Type,string Method)[] factories)
{
    var folder=Path.Combine(workspace,name);Directory.CreateDirectory(folder);
    var inputs=new List<PluginBuildInput>();var bound=new List<RuntimePluginDefinition>();
    foreach(var entry in factories)
    {
        var definition=entry.Factory.Bind(null);var assemblies=new Dictionary<string,Assembly>(StringComparer.Ordinal);
        void Include(Assembly assembly)
        {
            var id=assembly.GetName().Name!;
            if(id is "CordiSharp" || id.StartsWith("System",StringComparison.Ordinal) || id is "netstandard" or "mscorlib")return;
            if(!assemblies.TryAdd(id,assembly))return;
            foreach(var reference in assembly.GetReferencedAssemblies())Include(Assembly.Load(reference));
        }
        Include(entry.Type.Assembly);
        var package=Path.Combine(folder,definition.Id);Directory.CreateDirectory(package);
        var files=assemblies.Values.Select(a=>
        {
            var path=Path.GetFileName(a.Location);File.Copy(a.Location,Path.Combine(package,path));
            return new PluginFile(path,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a.Location))),a==entry.Type.Assembly?"implementation":"managed");
        }).ToArray();
        var manifest=new PluginManifest(1,"runtime",definition.Id,definition.Version,definition.Api,
            new(Path.GetFileName(entry.Type.Assembly.Location),entry.Type.FullName!,entry.Method),["win-x64"],new(true,["win-x64"],"Static-registration test; native execution verified separately"),
            definition.Provides.Select(p=>new PluginCapability(p,1)).ToArray(),definition.Requires.Select(p=>new PluginCapability(p,1)).ToArray(),definition.Execution,files);
        inputs.Add(new(manifest,package));bound.Add(PluginRegistration.Bind(manifest,entry.Factory).Bind(null));
    }
    var generated=StaticRegistration.Generate(inputs,"win-x64",HostMode.Headless);
    if(generated.References.Any(p=>new[]{"Lua","Wasm","YamlDotNet","Editor","WindowsDX"}.Any(n=>Path.GetFileName(p).Contains(n,StringComparison.OrdinalIgnoreCase))))throw new Exception("Unselected implementation referenced");
    if(name=="nonboard" && generated.References.Any(p=>Path.GetFileName(p).StartsWith("Kernel",StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("BoardGame.",StringComparison.Ordinal)))throw new Exception("Nonboard references board implementation");
    var startup=new RuntimeStartup(1,"static-test","1",inputs.Select(i=>new RuntimePluginSelection(i.Manifest.Id,i.Manifest.Version,1,JsonDocument.Parse("{}").RootElement.Clone(),i.Manifest.Fingerprint)).ToList());
    var expected=Describe(startup,bound);
    File.WriteAllText(Path.Combine(folder,"Generated.cs"),generated.Source);
    File.WriteAllText(Path.Combine(folder,"startup.json"),JsonSerializer.Serialize(startup));
    File.WriteAllText(Path.Combine(folder,"Program.cs"),"""
using BoardGame.Runtime;
using BoardGame.Kernel;
using System.Text.Json;
var startup=JsonSerializer.Deserialize<RuntimeStartup>(File.ReadAllText(args[0]))!;
var definitions=GeneratedCatalog.Create().Select(p=>p.Bind(null)).ToArray();
Console.WriteLine(Describe(startup,definitions));
"""+DescriptionSource());
    var project=new XElement("Project",new XAttribute("Sdk","Microsoft.NET.Sdk"),
        new XElement("PropertyGroup",new XElement("OutputType","Exe"),new XElement("EnableDefaultCompileItems","false")),
        new XElement("ItemGroup",new XElement("Compile",new XAttribute("Include","Program.cs")),new XElement("Compile",new XAttribute("Include","Generated.cs")),
            new XElement("ProjectReference",new XAttribute("Include",Path.Combine(repo,"src/Runtime.Plugins/Runtime.Plugins.csproj")))),XElement.Parse(generated.ProjectItems));
    File.WriteAllText(Path.Combine(folder,"Generated.csproj"),project.ToString());
    var start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
    foreach(var arg in new[]{"run","--project",Path.Combine(folder,"Generated.csproj"),"-c","Release","--",Path.Combine(folder,"startup.json")})start.ArgumentList.Add(arg);
    using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync();var result=(await output).Trim();var errors=await error;
    File.WriteAllText(Path.Combine(folder,"run.log"),result+Environment.NewLine+errors);
    if(child.ExitCode!=0 || result!=expected)throw new Exception(name+": generated registration differs: "+result+errors+"\nEXPECTED "+expected);
}
static string Describe(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> definitions)=>
    string.Join(';',RuntimeHost.Validate(startup,definitions,[]).Select(p=>$"{p.Id}|{p.Implementation}|{p.Execution.Scope}|{string.Join(',',p.Provides)}|{string.Join(',',p.Requires)}"))+"/"+
    string.Join(',',PhaseOrder.Sort(definitions.SelectMany(p=>(p.Execution.Contributions ?? []).Select(c=>new ScheduledContribution(p.Id,p.Execution.Scope,c)))).Select(c=>$"{c.Plugin}/{c.Declaration.Id}"));

static string DescriptionSource()=>"""
static string Describe(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> definitions)=>
    string.Join(';',RuntimeHost.Validate(startup,definitions,[]).Select(p=>$"{p.Id}|{p.Implementation}|{p.Execution.Scope}|{string.Join(',',p.Provides)}|{string.Join(',',p.Requires)}"))+"/"+
    string.Join(',',PhaseOrder.Sort(definitions.SelectMany(p=>(p.Execution.Contributions ?? []).Select(c=>new ScheduledContribution(p.Id,p.Execution.Scope,c)))).Select(c=>$"{c.Plugin}/{c.Declaration.Id}"));
""";
