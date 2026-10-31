using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BoardGame.Plugins;

namespace BoardGame.Publishing;

public sealed record NativeEntry(string Type,string Method,string Title,string Version,byte[]? Icon=null);
public sealed record NativeBuildProgress(string Stage,string Message);
public sealed record NativeBuildOptions(string Destination,NativeToolchainOptions? Toolchain=null,string? DiagnosticsDirectory=null);
public sealed record NativeBuildResult(bool Success,string? Directory,string Log,IReadOnlyList<NativeBuildDiagnostic> Diagnostics,TimeSpan Duration);

/// <summary>Author-side service. Inputs are precompiled implementations; only the fixed entry and static catalogue are generated.</summary>
public static class NativeBuildService
{
    public static async Task<NativeBuildResult> Build(PublicationPlan plan,NativeEntry entry,NativeBuildOptions options,
        IProgress<NativeBuildProgress>? progress=null,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();entry=entry with {Icon=entry.Icon?.ToArray()};ValidateEntry(entry);
        var reserved=new HashSet<string>(["Play.exe","Play.dll","game.ico","native-release.json","build-inputs.json","startup-error.txt"],StringComparer.OrdinalIgnoreCase);
        if(plan.Files.Any(f=>reserved.Contains(f.Name)))throw new InvalidDataException("Runtime input conflicts with a reserved release filename");
        var destination=Path.GetFullPath(options.Destination);
        if(Directory.Exists(destination) || File.Exists(destination))throw new IOException("Release destination already exists: "+destination);
        var watch=Stopwatch.StartNew();var diagnostics=new List<NativeBuildDiagnostic>();
        var id=Guid.NewGuid().ToString("N");
        var logs=Path.Combine(Path.GetFullPath(options.DiagnosticsDirectory ?? Path.Combine(Path.GetTempPath(),"bgb-native-build-logs")),id);
        Directory.CreateDirectory(logs);var log=Path.Combine(logs,"build.log");
        var workspace=Path.Combine(Path.GetTempPath(),"bgb-native-build-"+id);
        var stage=Path.Combine(Path.GetDirectoryName(destination)!,"."+Path.GetFileName(destination)+".building-"+id);
        var success=false;var workspaceOwned=false;var stageOwned=false;
        using var writer=new StreamWriter(log){AutoFlush=true};
        void Report(string phase,string text){writer.WriteLine("["+phase+"] "+text);progress?.Report(new(phase,text));}
        try
        {
            Report("toolchain","Checking Windows x64 native toolchain");
            var toolchain=await NativeToolchain.Check(plan.Target,options.Toolchain,cancellation).ConfigureAwait(false);
            if(!toolchain.Available)diagnostics.AddRange(toolchain.Diagnostics);
            else
            {
                Report("inputs","Copying and verifying selected runtime inputs");
                if(Directory.Exists(workspace) || File.Exists(workspace))throw new IOException("Build workspace already exists: "+workspace);
                workspaceOwned=true;
                var prepared=await PublicationInputs.Create(plan,workspace,cancellation).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(workspace,"global.json"),JsonSerializer.Serialize(new {sdk=new {version=toolchain.Sdk,rollForward="disable"}}),cancellation).ConfigureAwait(false);
                var integrity=NativeReleaseGuard.Describe(prepared.ContentFiles.Select(path=>new NativeReleaseFile("content/"+path,Hash(Path.Combine(workspace,"content",path))))
                    .Concat(prepared.RuntimeFiles.Where(f=>f.Role is "native" or "content").Select(f=>new NativeReleaseFile(f.Name,f.Sha256)))
                    .Concat(entry.Icon==null?[]:new[]{new NativeReleaseFile("game.ico",Convert.ToHexStringLower(SHA256.HashData(entry.Icon)))}));
                await File.WriteAllTextAsync(Path.Combine(workspace,"Program.cs"),Program(entry,integrity,plan.Mode==BoardGame.Runtime.HostMode.Graphical),cancellation).ConfigureAwait(false);
                using(var manifest=typeof(NativeBuildService).Assembly.GetManifestResourceStream("Runtime.Publishing.NativeApplication.manifest") ?? throw new InvalidDataException("Missing native application manifest"))
                using(var output=File.Create(Path.Combine(workspace,"app.manifest")))await manifest.CopyToAsync(output,cancellation).ConfigureAwait(false);
                if(entry.Icon!=null)await File.WriteAllBytesAsync(Path.Combine(workspace,"game.ico"),entry.Icon,cancellation).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(workspace,"Game.csproj"),Project(prepared,entry,toolchain),cancellation).ConfigureAwait(false);
                foreach(var name in new[]{"Game.csproj","Program.cs","GeneratedCatalog.cs","global.json","build-inputs.json","app.manifest"})
                    File.Copy(Path.Combine(workspace,name),Path.Combine(logs,name));
                var compilerDirectory=Path.GetDirectoryName(toolchain.Compiler)!;
                var vcRoot=Path.GetFullPath(Path.Combine(compilerDirectory,"../../.."));
                var environment=new Dictionary<string,string>
                {
                    ["PATH"]=compilerDirectory+Path.PathSeparator+Path.Combine(toolchain.WindowsKits,"bin",toolchain.WindowsSdk,"x64")+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"),
                    ["LIB"]=string.Join(Path.PathSeparator,new[]{Path.Combine(vcRoot,"lib/x64"),Path.Combine(toolchain.WindowsKits,"Lib",toolchain.WindowsSdk,"um/x64"),Path.Combine(toolchain.WindowsKits,"Lib",toolchain.WindowsSdk,"ucrt/x64")}),
                    ["MSBUILDDISABLENODEREUSE"]="1",["DOTNET_CLI_TELEMETRY_OPTOUT"]="1",["DOTNET_NOLOGO"]="1"
                };
                Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
                if(Directory.Exists(stage) || File.Exists(stage))throw new IOException("Build staging directory already exists: "+stage);
                Directory.CreateDirectory(stage);stageOwned=true;
                Report("build","Publishing with SDK "+toolchain.Sdk+", NativeAOT "+toolchain.Runtime+", linker "+toolchain.Linker);
                var build=await ToolProcess.Run(toolchain.Dotnet,["publish","Game.csproj","-c","Release","-r","win-x64","--self-contained","true","-o",stage,"-nr:false","-p:UseSharedCompilation=false","-p:ImportDirectoryBuildProps=false","-p:ImportDirectoryBuildTargets=false"],workspace,cancellation,
                    line=>Report("build",line),environment).ConfigureAwait(false);
                if(File.Exists(Path.Combine(workspace,"packages.lock.json")))File.Copy(Path.Combine(workspace,"packages.lock.json"),Path.Combine(logs,"packages.lock.json"));
                if(build.ExitCode!=0)
                {
                    diagnostics.Add(new("build.failed","build","NativeAOT compiler/linker failed (exit "+build.ExitCode+"); inspect "+log));
                    foreach(var error in build.Output.Split('\n').Where(l=>Regex.IsMatch(l,"\\berror (IL|CS|LNK)[0-9]+",RegexOptions.CultureInvariant)).Distinct(StringComparer.Ordinal).Take(20))
                    {
                        var owners=plan.Files.Where(f=>error.Contains(Path.GetFileNameWithoutExtension(f.Name),StringComparison.Ordinal)).Select(f=>f.Owner).Distinct(StringComparer.Ordinal).ToArray();
                        diagnostics.Add(new("build.compiler","build",(owners.Length>0?string.Join(", ",owners)+": ":"")+error.Trim()));
                    }
                }
                else
                {
                    cancellation.ThrowIfCancellationRequested();Report("verify","Checking native output and complete release files");
                    VerifyOutput(stage,prepared);
                    if(entry.Icon!=null && !File.ReadAllBytes(Path.Combine(stage,"game.ico")).SequenceEqual(entry.Icon))throw new InvalidDataException("Published game icon differs");
                    File.Copy(Path.Combine(workspace,"build-inputs.json"),Path.Combine(stage,"build-inputs.json"));
                    var files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories).Select(path=>new
                    {path=Path.GetRelativePath(stage,path).Replace('\\','/'),bytes=new FileInfo(path).Length,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}).OrderBy(f=>f.path,StringComparer.Ordinal).ToArray();
                    var report=new {format=1,target=plan.Target,sourceFingerprint=plan.SourceFingerprint,title=entry.Title,version=entry.Version,
                        sdk=toolchain.Sdk,runtime=toolchain.Runtime,compiler=toolchain.Compiler,linker=toolchain.Linker,windowsSdk=toolchain.WindowsSdk,
                        compilerVersion=FileVersionInfo.GetVersionInfo(toolchain.Compiler).FileVersion,linkerVersion=FileVersionInfo.GetVersionInfo(toolchain.Linker).FileVersion,
                        strictWarnings=true,durationMs=watch.ElapsedMilliseconds,files};
                    await File.WriteAllTextAsync(Path.Combine(stage,"native-release.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}),cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();Directory.Move(stage,destination);success=true;
                    Report("complete","Native release created: "+destination);
                }
            }
        }
        catch(OperationCanceledException)
        {diagnostics.Add(new("build.cancelled","cancel","Native build cancelled; subprocesses were stopped and no release was published"));}
        catch(Exception error) when(error is not OutOfMemoryException)
        {diagnostics.Add(new("build.exception","build",error.Message));writer.WriteLine(error);}
        finally
        {
            foreach(var owned in new[]{(Path:workspace,Owned:workspaceOwned),(Path:stage,Owned:stageOwned)}.Where(p=>p.Owned).Select(p=>p.Path))
                try{if(Directory.Exists(owned))Directory.Delete(owned,true);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {diagnostics.Add(new("build.cleanup","cleanup",owned+": "+error.Message));}
        }
        return new(success,success?destination:null,log,diagnostics.AsReadOnly(),watch.Elapsed);
    }
    public static void ValidateEntry(NativeEntry entry)
    {
        bool Name(string part)=>Regex.IsMatch(part,"\\A[A-Za-z_][A-Za-z0-9_]*\\z",RegexOptions.CultureInvariant);
        if(string.IsNullOrWhiteSpace(entry.Type) || !entry.Type.Split('.').All(Name) || !Name(entry.Method) || string.IsNullOrWhiteSpace(entry.Title) || entry.Title.Length>128 || string.IsNullOrWhiteSpace(entry.Version) || entry.Version.Length>80)
            throw new InvalidDataException("Invalid native entry type, method, title or version");
        if(entry.Icon!=null && (entry.Icon.Length<6 || entry.Icon.Length>4*1024*1024 || entry.Icon[0]!=0 || entry.Icon[1]!=0 || entry.Icon[2]!=1 || entry.Icon[3]!=0 || BitConverter.ToUInt16(entry.Icon,4)==0))
            throw new InvalidDataException("Native application icon must be an ICO file");
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
    private static string Program(NativeEntry entry,string integrity,bool graphical)=>"""
using System;
using System.IO;
internal static class NativeMain
{
    [STAThread] private static int Main()
    {
        try
        {
"""+"            BoardGame.Plugins.NativeReleaseGuard.Verify(AppContext.BaseDirectory, "+JsonSerializer.Serialize(integrity)+");\n"+
"            return global::"+string.Join('.',entry.Type.Split('.').Select(s=>"@"+s))+".@"+entry.Method+"(GeneratedCatalog.Create(), Path.Combine(AppContext.BaseDirectory, \"content\"), "+JsonSerializer.Serialize(entry.Title)+");\n"+"""
        }
        catch(Exception error)
        {
            try {File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"startup-error.txt"),error.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}
"""+(graphical?"            MessageBoxW(0,error.Message,\"游戏无法启动\",0x10);\n":"")+"""
            return 1;
        }
    }
"""+(graphical?"    [System.Runtime.InteropServices.DllImport(\"user32.dll\",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] private static extern int MessageBoxW(nint owner,string text,string title,uint flags);\n":"")+"}\n";
    private static string Project(PreparedPublication input,NativeEntry entry,NativeToolchainInfo toolchain)
    {
        var properties=new XElement("PropertyGroup",
            new XElement("TargetFramework","net10.0-windows"),new XElement("OutputType","WinExe"),new XElement("AssemblyName","Play"),
            new XElement("ApplicationManifest","app.manifest"),
            new XElement("ImplicitUsings","enable"),new XElement("Nullable","enable"),new XElement("EnableDefaultCompileItems","false"),
            new XElement("ImportDirectoryBuildProps","false"),new XElement("ImportDirectoryBuildTargets","false"),
            new XElement("PublishAot","true"),new XElement("RuntimeFrameworkVersion",toolchain.Runtime),new XElement("IlcUseEnvironmentalTools","true"),
            new XElement("TreatWarningsAsErrors","true"),new XElement("ILLinkTreatWarningsAsErrors","true"),new XElement("IlcTreatWarningsAsErrors","true"),new XElement("TrimmerSingleWarn","false"),
            new XElement("RestorePackagesWithLockFile","true"),new XElement("Deterministic","true"),new XElement("DebugSymbols","false"),new XElement("DebugType","none"),
            new XElement("NativeDebugSymbols","false"),new XElement("CopyOutputSymbolsToPublishDirectory","false"),
            new XElement("AssemblyTitle",entry.Title),new XElement("Product",entry.Title),new XElement("InformationalVersion",entry.Version),new XElement("AssemblyVersion","1.0.0.0"));
        if(entry.Icon!=null)properties.Add(new XElement("ApplicationIcon","game.ico"));
        var content=new XElement("ItemGroup",new XElement("Compile",new XAttribute("Include","Program.cs;GeneratedCatalog.cs")));
        if(entry.Icon!=null)content.Add(new XElement("Content",new XAttribute("Include","game.ico"),new XElement("CopyToPublishDirectory","Always")));
        foreach(var file in input.RuntimeFiles.Where(f=>f.Role is "native" or "content"))content.Add(new XElement("Content",new XAttribute("Include","runtime/"+file.Name),new XElement("Link",file.Name),new XElement("CopyToPublishDirectory","Always")));
        foreach(var path in input.ContentFiles)content.Add(new XElement("Content",new XAttribute("Include","content/"+path),new XElement("CopyToPublishDirectory","Always")));
        return new XElement("Project",new XAttribute("Sdk","Microsoft.NET.Sdk"),properties,XElement.Parse(input.ProjectItems),content).ToString();
    }
    private static void VerifyOutput(string directory,PreparedPublication input)
    {
        var executable=Path.Combine(directory,"Play.exe");
        using(var stream=File.OpenRead(executable))
        using(var pe=new PEReader(stream))
            if(pe.HasMetadata || pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64 || pe.PEHeaders.PEHeader?.Subsystem!=Subsystem.WindowsGui)
                throw new InvalidDataException("Output is not a Windows x64 native GUI executable");
        foreach(var file in input.RuntimeFiles.Where(f=>f.Role is "native" or "content"))PublicationPlanner.Verify(file with {Source=Path.Combine(directory,file.Name)});
        foreach(var path in input.ContentFiles)
            if(!File.ReadAllBytes(Path.Combine(input.Directory,"content",path)).SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"content",path))))
                throw new InvalidDataException("Published content differs: "+path);
        foreach(var path in Directory.GetFiles(directory,"*",SearchOption.TopDirectoryOnly))
        {
            if(Path.GetExtension(path).Equals(".pdb",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Debug symbols unexpectedly entered native release: "+path);
            if(Path.GetExtension(path).Equals(".dll",StringComparison.OrdinalIgnoreCase))
            {
                using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
                if(pe.HasMetadata || Path.GetFileName(path).Equals("coreclr.dll",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Managed runtime/assembly unexpectedly entered native release: "+path);
            }
        }
    }
}
