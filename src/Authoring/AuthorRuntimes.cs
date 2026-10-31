using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.Plugins;

namespace BoardGame.Authoring;

public interface IAuthorRuntime
{
    string Id {get;}
    bool Supports(IAuthorFiles files);
    Task<IReadOnlyList<string>> Validate(ProjectView view,string layer,CancellationToken cancellation=default);
}
public interface IAuthorPackageRuntime : IAuthorRuntime
{
    string Pack(ProjectView view,string destination,CancellationToken cancellation=default);
}
public interface IAuthorApplicationRuntime : IAuthorRuntime
{
    ApplicationCatalog Catalog {get;}
    AuthorApplicationLease Open(ProjectView view,IEnumerable<ApprovedApplicationPlugin>? additional=null);
}
public sealed class AuthorApplicationLease(ApplicationCatalog catalog,PluginCatalogLease? imported=null):IDisposable
{
    public ApplicationCatalog Catalog=>imported?.Catalog ?? catalog;
    public IReadOnlyList<PluginBuildInput> Imports=>imported?.Inputs ?? [];
    public void Dispose()=>imported?.Dispose();
}
public sealed class AuthorRuntimes
{
    private readonly Dictionary<string,IAuthorRuntime> providers=new(StringComparer.Ordinal);
    public IDisposable Register(IAuthorRuntime provider)
    {
        if(!providers.TryAdd(provider.Id,provider))throw new InvalidOperationException("Duplicate author runtime: "+provider.Id);
        return new AuthorCleanup(()=>providers.Remove(provider.Id));
    }
    public IAuthorRuntime Resolve(IAuthorFiles files)
    {
        var startup=JsonSerializer.Deserialize<RuntimeStartup>(files.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json);
        if(startup is not {Format:1,Plugins:not null} || string.IsNullOrWhiteSpace(startup.Id) || string.IsNullOrWhiteSpace(startup.Version) ||
            startup.Plugins.Any(p=>p==null || string.IsNullOrWhiteSpace(p.Id)))
            throw new AuthorOperationException("entry.invalid","Invalid startup identity or plugin list",BoardGame.AppRuntime.GameRuntime.Entry);
        var matches=providers.Values.Where(p=>p.Supports(files)).ToArray();
        return matches.Length==1?matches[0]:throw new AuthorOperationException("runtime.unavailable",matches.Length==0?"No approved author runtime supports this content":"Ambiguous author runtime providers");
    }
    public void ValidateContent(ProjectView view)=>Resolve(view).Validate(view,"content").GetAwaiter().GetResult();
}
public sealed class ApplicationAuthorRuntime(string id,ApplicationCatalog catalog,PluginStore? installation=null,bool importedDefinitions=false) : IAuthorPackageRuntime,IAuthorPlayerRuntime,IAuthorApplicationRuntime
{
    public string Id=>id;
    public ApplicationCatalog Catalog=>BoardGame.Rendering.GraphicsBackendContract.Extend(catalog);
    public AuthorApplicationLease Open(ProjectView view,IEnumerable<ApprovedApplicationPlugin>? additional=null)
    {
        var selected=BoardGame.Rendering.GraphicsBackendContract.Extend(additional==null?catalog:
            catalog.Extend(additional.Where(p=>catalog.Content.Plugins.All(c=>c.Id!=p.Bind(null).Id))));
        return new(selected,installation?.Open(JsonSerializer.Deserialize<RuntimeStartup>(view.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json) ?? throw new InvalidDataException("Invalid startup"),selected));
    }
    public void PreflightPlayer(string release,ProjectView view,CancellationToken cancellation)
    {cancellation.ThrowIfCancellationRequested();using var lease=Open(view);using var snapshot=new ProjectSnapshot(view);_=PluginPlayerDistribution.Preflight(release,PlayerSource(snapshot.Directory,lease.Catalog),lease.Imports);}
    public string ExportPlayer(string release,ProjectView view,string target,AuthorExportRequest request)
    {
        using var lease=Open(view);
        return ExportSnapshots.Use(view,request,snapshot=>
        {
            var source=PlayerSource(snapshot.Directory,lease.Catalog);
            PluginPlayerDistribution.Export(release,source,lease.Imports,target,path=>request.AdditionalSteps?.Invoke(snapshot.Directory,path),request.Progress,request.Cancellation);
            return source.Fingerprint;
        });
    }
    private static PlayerPackageSource PlayerSource(string directory,ApplicationCatalog catalog)
    {
        var content=BoardGame.AppRuntime.GameRuntime.Load(directory,catalog.Content);
        return new(content.Identity.Startup,3,content.Fingerprint,p=>BoardGame.AppRuntime.GamePackage.Pack(directory,p,catalog.Content),
            p=>BoardGame.AppRuntime.GamePackage.Load(p,catalog.Content).Fingerprint,BoardGame.Rendering.GraphicsBackendContract.DistributionFiles(content.Identity.Startup),
            "Windows x64\r\n双击 Play.exe 运行。无需作者工具、SDK 或系统 .NET。"+BoardGame.Rendering.GraphicsBackendContract.PlatformRequirements(content.Identity.Startup)+"\r\n",
            JsonSerializer.SerializeToElement(new{content.Identity.Startup.Id,content.Identity.Startup.Version,content.Fingerprint},AuthorProfiles.Json));
    }
    public string Pack(ProjectView view,string destination,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=Open(view);var catalog=lease.Catalog;
        using var snapshot=new ProjectSnapshot(view);
        BoardGame.AppRuntime.GamePackage.Pack(snapshot.Directory,destination,catalog.Content);
        return BoardGame.AppRuntime.GamePackage.Load(destination,catalog.Content).Fingerprint;
    }
    public bool Supports(IAuthorFiles files)
    {
        var definitions=catalog.Content.Plugins.Where(p=>p.Provides.Contains("definition")).Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        if(importedDefinitions)foreach(var manifest in installation?.Installed() ?? [])if(manifest.Provides.Any(p=>p.Name=="definition"))definitions.Add(manifest.Id);
        return JsonSerializer.Deserialize<RuntimeStartup>(files.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json)!.Plugins.Any(p=>definitions.Contains(p.Id));
    }
    public async Task<IReadOnlyList<string>> Validate(ProjectView view,string layer,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var lease=Open(view);var catalog=lease.Catalog;
        var startup=JsonSerializer.Deserialize<RuntimeStartup>(view.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json)!;
        RuntimeDiscovery.Validate(startup,catalog.Content.Plugins);
        if(layer=="configuration")return [];
        var content=BoardGame.AppRuntime.GameRuntime.LoadFiles(view.CopyFiles(),catalog.Content);
        if(layer!="runtime")return [];
        await using var host=await catalog.StartAsync(content,clock:new(TimeSpan.FromSeconds(1d/60)));
        host.Driver.Paused=true;
        await host.Driver.StepAsync(cancellation);
        if(host.Driver.Fault is {} fault)throw new AuthorOperationException("runtime.update",fault.Error.Message);
        return ["initial-application-update"];
    }
}
