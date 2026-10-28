using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public interface IAuthorRuntime
{
    string Id {get;}
    bool Supports(IAuthorFiles files);
    Task<IReadOnlyList<string>> Validate(ProjectView view,string layer,CancellationToken cancellation=default);
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
public sealed class ApplicationAuthorRuntime(string id,ApplicationCatalog catalog) : IAuthorRuntime
{
    public string Id=>id;
    public ApplicationCatalog Catalog=>catalog;
    public bool Supports(IAuthorFiles files)
    {
        var definitions=catalog.Content.Plugins.Where(p=>p.Provides.Contains("definition")).Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.Deserialize<RuntimeStartup>(files.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json)!.Plugins.Any(p=>definitions.Contains(p.Id));
    }
    public async Task<IReadOnlyList<string>> Validate(ProjectView view,string layer,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
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
