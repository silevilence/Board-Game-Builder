using System.Runtime.InteropServices;

namespace BoardGame.Publishing;

public sealed record NativeBuildDiagnostic(string Code,string Stage,string Message);
public sealed record NativeToolchainOptions(string Dotnet="dotnet",string? VsWhere=null,string? VisualStudio=null,string? WindowsKits=null);
public sealed record NativeToolchainInfo(string Dotnet,string Sdk,string Runtime,string Compiler,string Linker,string WindowsSdk,string WindowsKits,
    IReadOnlyList<NativeBuildDiagnostic> Diagnostics)
{public bool Available=>Diagnostics.Count==0;}

public static class NativeToolchain
{
    public const string SdkVersion="10.0.112";
    public const string RuntimeVersion="10.0.12";
    public static async Task<NativeToolchainInfo> Check(string target,NativeToolchainOptions? options=null,CancellationToken cancellation=default)
    {
        options ??=new();var diagnostics=new List<NativeBuildDiagnostic>();
        void Error(string code,string message)=>diagnostics.Add(new(code,"toolchain",message));
        if(target!="win-x64" || !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture!=Architecture.X64)
        {
            Error("toolchain.target","Only validated Windows x64 local builds are supported; requested "+target);
            return new(options.Dotnet,SdkVersion,RuntimeVersion,"","","","",diagnostics);
        }
        var dotnet=options.Dotnet;
        try
        {
            var result=await ToolProcess.Run(dotnet,["--list-sdks"],Path.GetTempPath(),cancellation).ConfigureAwait(false);
            var sdk=result.Output.Split('\n').Select(l=>l.Trim()).SingleOrDefault(l=>l.StartsWith(SdkVersion+" [",StringComparison.Ordinal));
            if(result.ExitCode!=0 || sdk==null)Error("toolchain.sdk","Install the validated .NET SDK "+SdkVersion+" (x64); no SDK roll-forward or ordinary Player fallback is used");
            else
            {
                var root=sdk[(sdk.IndexOf('[')+1)..sdk.LastIndexOf(']')];
                var host=Path.Combine(Path.GetDirectoryName(root)!,"dotnet.exe");
                if(!File.Exists(host))Error("toolchain.sdk-host","SDK directory has no matching dotnet host: "+host);else dotnet=host;
            }
        }
        catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
        {Error("toolchain.sdk","Cannot inspect .NET SDK: "+error.Message+"; install SDK "+SdkVersion);}
        var installation=options.VisualStudio;
        if(installation==null)
        {
            var vswhere=options.VsWhere ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft Visual Studio/Installer/vswhere.exe");
            try
            {
                if(!File.Exists(vswhere))Error("toolchain.vs","Install Visual Studio C++ desktop tools (including the x64 compiler/linker and Windows SDK)");
                else
                {
                    var result=await ToolProcess.Run(vswhere,["-latest","-products","*","-requires","Microsoft.VisualStudio.Component.VC.Tools.x86.x64","-property","installationPath"],Path.GetTempPath(),cancellation).ConfigureAwait(false);
                    installation=result.Output.Trim();
                    if(result.ExitCode!=0 || installation.Length==0)Error("toolchain.vs","No Visual Studio installation with VC.Tools.x86.x64 was found");
                }
            }
            catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException){Error("toolchain.vs",error.Message);}
        }
        var compiler="";var linker="";
        if(!string.IsNullOrWhiteSpace(installation))
        {
            var tools=Path.Combine(installation,"VC/Tools/MSVC");
            var versions=Directory.Exists(tools)?Directory.GetDirectories(tools).Where(p=>Version.TryParse(Path.GetFileName(p),out _)).OrderByDescending(p=>Version.Parse(Path.GetFileName(p))).ToArray():[];
            var selected=versions.FirstOrDefault(p=>File.Exists(Path.Combine(p,"bin/Hostx64/x64/cl.exe")) && File.Exists(Path.Combine(p,"bin/Hostx64/x64/link.exe")));
            if(selected==null)Error("toolchain.linker","Missing x64 C++ compiler/linker in "+tools+"; install the Visual Studio C++ desktop workload");
            else {compiler=Path.Combine(selected,"bin/Hostx64/x64/cl.exe");linker=Path.Combine(selected,"bin/Hostx64/x64/link.exe");}
        }
        var kits=Path.GetFullPath(options.WindowsKits ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Windows Kits/10"));
        var libraries=Path.Combine(kits,"Lib");
        var sdkVersion=Directory.Exists(libraries)?Directory.GetDirectories(libraries).Select(Path.GetFileName).OfType<string>()
            .Where(v=>Version.TryParse(v,out _) && File.Exists(Path.Combine(libraries,v,"um/x64/kernel32.lib")) && File.Exists(Path.Combine(libraries,v,"ucrt/x64/ucrt.lib")))
            .OrderByDescending(Version.Parse).FirstOrDefault() ?? "":"";
        if(sdkVersion.Length==0)Error("toolchain.windows-sdk","Install Windows SDK x64 UM and UCRT libraries; not found under "+kits);
        cancellation.ThrowIfCancellationRequested();
        return new(dotnet,SdkVersion,RuntimeVersion,compiler,linker,sdkVersion,kits,diagnostics);
    }
}
