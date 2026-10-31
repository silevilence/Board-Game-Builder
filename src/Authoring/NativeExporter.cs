using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Publishing;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;

namespace BoardGame.Authoring;

/// <summary>Optional author provider. Only this provider inspects a native toolchain or invokes compilation.</summary>
public sealed class NativeExporter : IAuthorExporter
{
    public AuthorExportInfo Info {get;}=new("native-directory","author.export.native","1",1,"NativeAOT 原生游戏","native-directory",
        "按所选运行插件编译 Windows x64 原生游戏，双击 Play.exe 运行；需要固定 .NET SDK 和 C++ 工具链。",["win-x64"],
        ["Verified native runtime SDK",".NET SDK 10.0.112","Visual Studio x64 C++ tools and Windows SDK"],
        new("native.export","author.export.native","Compile the selected project snapshot to a native game; never fall back to a precompiled Player",
            new(){["target"]=new("string",true,"New independent output directory"),["sdk"]=new("string",false,"Maintainer native runtime distribution; default bundled native-sdk directory"),
                ["host"]=new("string",false,"Native host profile; default application.desktop"),["platform"]=new("string",false,"Target platform; only win-x64 is verified"),
                ["title"]=new("string",false,"EXE product title; defaults to game identity"),["version"]=new("string",false,"EXE product version; defaults to game version"),
                ["icon"]=new("string",false,"ICO path inside the captured project snapshot"),["dotnet"]=new("string",false,"Build machine dotnet executable"),
                ["visualStudio"]=new("string",false,"Visual Studio installation directory"),["windowsKits"]=new("string",false,"Windows Kits installation directory")},
            new(){["type"]="object",["description"]="source, target, native-directory kind and source content fingerprint"},["Native runtime distribution and native compiler"],["artifact"]));
    public static AuthorPlugin Plugin=>ExportProviderPlugin.Create(new NativeExporter());
    public async Task<AuthorExportCheck> Preflight(AuthorExportRequest request)
    {
        try
        {
            request.Cancellation.ThrowIfCancellationRequested();_=ExportTargets.NewTarget(request);
            var prepared=Prepare(request);
            var toolchain=await NativeToolchain.Check(prepared.Plan.Target,Options(request),request.Cancellation).ConfigureAwait(false);
            return new(request.View.Source,Info.Id,toolchain.Available,toolchain.Diagnostics.Select(d=>new AuthorDiagnostic(d.Code,d.Message)).ToArray());
        }
        catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
        {return new(request.View.Source,Info.Id,false,[new(error is AuthorOperationException operation?operation.Code:"export.native",error.Message)]);}
    }
    public async Task<AuthorArtifact> Export(AuthorExportRequest request)
    {
        request.Cancellation.ThrowIfCancellationRequested();var target=ExportTargets.NewTarget(request);var prepared=Prepare(request);
        var result=await NativeBuildService.Build(prepared.Plan,prepared.Entry,new(target,Options(request)),
            new ForwardProgress(request.Progress),request.Cancellation).ConfigureAwait(false);
        if(!result.Success)
        {
            if(result.Diagnostics.Any(d=>d.Code=="build.cancelled"))throw new OperationCanceledException(request.Cancellation);
            throw new AuthorOperationException("export.native-build",string.Join("; ",result.Diagnostics.Select(d=>d.Code+": "+d.Message))+"; log="+result.Log);
        }
        request.Progress?.Report("构建日志："+result.Log);
        return new(request.View.Source,target,Info.ArtifactKind,prepared.Plan.SourceFingerprint)
            {Log=result.Log,Warnings=result.Diagnostics.Select(d=>d.Code+": "+d.Message).ToArray()};
    }
    private static (PublicationPlan Plan,NativeEntry Entry) Prepare(AuthorExportRequest request)
    {
        var sdk=RuntimeSdk.Open(Path.GetFullPath(Value(request,"sdk") ?? Path.Combine(AppContext.BaseDirectory,"native-sdk")));
        if((Value(request,"platform") ?? "win-x64")!=sdk.Target)throw new AuthorOperationException("toolchain.target","Only the SDK's verified win-x64 target is available");
        var runtime=request.Runtimes.Resolve(request.View) as IAuthorApplicationRuntime
            ?? throw new AuthorOperationException("export.native-runtime","The selected author runtime has no approved application catalogue for native publication");
        var startup=JsonSerializer.Deserialize<RuntimeStartup>(request.View.Text(GameRuntime.Entry),AuthorProfiles.Json)
            ?? throw new AuthorOperationException("entry.invalid","Missing startup identity");
        var definitions=runtime.Catalog.Content.Plugins.ToList();
        // Validate graphics identities/configuration without opening a device in an author/export process.
        // Active instance code always comes from the already approved author runtime.
        foreach(var selection in startup.Plugins.Where(p=>definitions.All(d=>d.Id!=p.Id)))
        {
            var candidate=sdk.Plugins.SingleOrDefault(p=>p.Input.Manifest.Id==selection.Id && p.Input.Manifest.Version==selection.Version && p.Input.Manifest.Api==selection.Api);
            if(candidate==null)continue;
            var manifest=candidate.Input.Manifest;
            if(manifest.Execution.Scope==RuntimeScope.Instance && (manifest.Execution.Modes & HostMode.Headless)!=0)continue;
            if(selection.Implementation!=null && selection.Implementation!=manifest.Fingerprint)throw new AuthorOperationException("plugin.identity",selection.Id+": implementation content identity mismatch");
            definitions.Add(PluginRegistration.IdentityOnly(manifest).Bind(null) with {Implementation=selection.Implementation});
        }
        var content=GameRuntime.LoadFiles(request.View.CopyFiles(),new(definitions));
        var profile=Value(request,"host") ?? "application.desktop";
        // Backend enablement must be in game.start.json; author build options are not a second plugin selection.
        var plan=sdk.Plan(content,profile,[]);
        byte[]? icon=null;
        if(Value(request,"icon") is {} path){ProjectPaths.Validate(path);icon=request.View.Bytes(path);}
        return(plan,sdk.Entry(profile,Value(request,"title") ?? startup.Id,Value(request,"version") ?? startup.Version,icon));
    }
    private static string? Value(AuthorExportRequest request,string key)=>request.Arguments.TryGetProperty(key,out var value)?value.GetString():null;
    private static NativeToolchainOptions Options(AuthorExportRequest request)=>new(Value(request,"dotnet") ?? "dotnet",VisualStudio:Value(request,"visualStudio"),WindowsKits:Value(request,"windowsKits"));
    private sealed class ForwardProgress(IProgress<string>? progress):IProgress<NativeBuildProgress>
    {public void Report(NativeBuildProgress value)=>progress?.Report(value.Stage+": "+value.Message);}
}
