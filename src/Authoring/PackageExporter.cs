namespace BoardGame.Authoring;

public sealed class PackageExporter : IAuthorExporter
{
    public AuthorExportInfo Info {get;}=new("game-package","author.export.package","1",1,"游戏数据包","game-package",
        "游戏数据、脚本与资源；需匹配版本及插件能力的 Player 加载，不包含运行引擎。",["any"],["Compatible receiving Player and installed runtime plugins"],
        new("project.pack","author.export.package","Package the selected project version using its runtime package protocol",
            new(){["target"]=new("string",true,"New .bgm output file outside the project")},
            new(){["type"]="object",["description"]="source, target, kind and runtime fingerprint"},["Installed package runtime"],["artifact"]));
    public static AuthorPlugin Plugin=>ExportProviderPlugin.Create(new PackageExporter());
    public async Task<AuthorExportCheck> Preflight(AuthorExportRequest request)
    {
        try
        {
            _=ExportTargets.NewTarget(request);var runtime=Runtime(request);
            await runtime.Validate(request.View,"content",request.Cancellation).ConfigureAwait(false);
            return new(request.View.Source,Info.Id,true,[]);
        }
        catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
        {return new(request.View.Source,Info.Id,false,[new(error is AuthorOperationException operation?operation.Code:"export.package",error.Message)]);}
    }
    public Task<AuthorArtifact> Export(AuthorExportRequest request)
    {
        var target=ExportTargets.NewTarget(request);var temporary=target+".staging-"+Guid.NewGuid().ToString("N")+".bgm";
        try
        {
            request.Cancellation.ThrowIfCancellationRequested();request.Progress?.Report("打包指定工程版本…");
            var fingerprint=Runtime(request).Pack(request.View,temporary,request.Cancellation);
            request.Cancellation.ThrowIfCancellationRequested();File.Move(temporary,target,false);
            return Task.FromResult(new AuthorArtifact(request.View.Source,target,Info.ArtifactKind,fingerprint));
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static IAuthorPackageRuntime Runtime(AuthorExportRequest request)=>request.Runtimes.Resolve(request.View) as IAuthorPackageRuntime
        ?? throw new AuthorOperationException("export.package-runtime","Selected author runtime does not provide package export");
}
public static class ExportProviderPlugin
{
    public static AuthorPlugin Create(IAuthorExporter provider)=>new(provider.Info.Plugin,provider.Info.Version,provider.Info.Api,[],["author.operations","author.exports"],scope=>
    {
        var exports=scope.Service<AuthorExports>("author.exports");scope.Own(exports.Register(provider));
        scope.Register(new(provider.Info.Operation,async call=>await exports.Export(provider.Info.Id,new(call.View,call.Runtimes,call.Arguments,call.Cancellation)).ConfigureAwait(false)));
    },provider.Info.Description){Operations=[provider.Info.Operation]};
}
internal static class ExportTargets
{
    public static string NewTarget(AuthorExportRequest request)
    {
        var target=Path.GetFullPath(request.Arguments.GetProperty("target").GetString()!);
        var source=request.View.Source.Identity.Directory is {} directory?Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)):null;
        if(source!=null && (target.Equals(source,StringComparison.OrdinalIgnoreCase) || target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            throw new AuthorOperationException("output.project","Choose an independent output outside the source project",target);
        if(File.Exists(target) || Directory.Exists(target))throw new AuthorOperationException("output.exists","Choose a new output path",target);
        return target;
    }
}
