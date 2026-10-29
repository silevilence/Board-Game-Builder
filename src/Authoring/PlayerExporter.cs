using BoardGame.Runtime;

namespace BoardGame.Authoring;

public interface IAuthorPlayerRuntime : IAuthorRuntime
{
    void PreflightPlayer(string release,ProjectView view,CancellationToken cancellation);
    string ExportPlayer(string release,ProjectView view,string target,AuthorExportRequest request);
}
public sealed class PlayerExporter : IAuthorExporter
{
    public AuthorExportInfo Info {get;}=new("player-directory","author.export.player","1",1,"独立 Player 游戏","player-directory",
        "包含预编译 Player 和运行依赖，双击 Play.exe 运行；导出无需 SDK。",["win-x64"],["Matching precompiled Player and runtime components"],
        new("player.export","author.export.player","Export an independent Windows Player directory using a precompiled compatible Player; no SDK invocation",
            new(){["target"]=new("string",true,"New independent output directory"),["player"]=new("string",false,"Compatible precompiled Player distribution; default bundled player directory"),
                ["components"]=new("string",false,"Verified release component ZIP directory"),["dependencyCache"]=new("string",false,"Export dependency cache"),["offline"]=new("boolean",false,"Do not download missing export components")},
            new(){["type"]="object",["description"]="source, target, kind and runtime fingerprint"},["Matching precompiled Player"],["artifact"]));
    public static AuthorPlugin Plugin=>ExportProviderPlugin.Create(new PlayerExporter());
    public Task<AuthorExportCheck> Preflight(AuthorExportRequest request)
    {
        try
        {
            _=ExportTargets.NewTarget(request);request.Cancellation.ThrowIfCancellationRequested();
            Runtime(request).PreflightPlayer(Release(request),request.View,request.Cancellation);
            return Task.FromResult(new AuthorExportCheck(request.View.Source,Info.Id,true,[]));
        }
        catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
        {return Task.FromResult(new AuthorExportCheck(request.View.Source,Info.Id,false,[new(error is AuthorOperationException operation?operation.Code:"export.player",error.Message)]));}
    }
    public Task<AuthorArtifact> Export(AuthorExportRequest request)
    {
        var target=ExportTargets.NewTarget(request);
        var warnings=new List<string>();
        var fingerprint=Runtime(request).ExportPlayer(Release(request),request.View,target,request with{Warning=text=>{warnings.Add(text);request.Warning?.Invoke(text);}});
        return Task.FromResult(new AuthorArtifact(request.View.Source,target,Info.ArtifactKind,fingerprint){Warnings=warnings});
    }
    private static IAuthorPlayerRuntime Runtime(AuthorExportRequest request)=>request.Runtimes.Resolve(request.View) as IAuthorPlayerRuntime
        ?? throw new AuthorOperationException("export.player-runtime","Selected author runtime does not provide standalone Player export");
    private static string Release(AuthorExportRequest request)
    {
        string? Value(string key)=>request.Arguments.TryGetProperty(key,out var value)?value.GetString():null;
        var offline=request.Arguments.TryGetProperty("offline",out var flag) && flag.GetBoolean();
        return ExportDependencies.Resolve(Path.GetFullPath(Value("player") ?? Path.Combine(AppContext.BaseDirectory,"player")),
            new(Value("dependencyCache"),Value("components"),!offline),request.Progress,request.Cancellation);
    }
}
internal static class ExportSnapshots
{
    public static string Use(ProjectView view,AuthorExportRequest request,Func<ProjectSnapshot,string> export)
    {
        var snapshot=new ProjectSnapshot(view);var completed=false;
        try {var result=export(snapshot);completed=true;return result;}
        finally
        {
            try {snapshot.Dispose();}
            catch(Exception error) when(completed && error is IOException or UnauthorizedAccessException)
            {request.Warning?.Invoke("临时文件清理失败："+snapshot.Directory+": "+error.Message);}
        }
    }
}
