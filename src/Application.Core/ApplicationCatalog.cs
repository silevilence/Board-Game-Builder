using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

/// <summary>Factories are supplied by the installed host, never resolved from a content path.</summary>
public sealed record ApprovedApplicationPlugin(Func<GameContent?,RuntimePluginDefinition> Bind);

public sealed class ApplicationCatalog
{
    private readonly ApprovedApplicationPlugin[] approved;
    public ContentCatalog Content {get;}
    public ApplicationCatalog(IEnumerable<ApprovedApplicationPlugin> plugins)
    {
        approved=plugins.ToArray();
        var descriptions=approved.Select(p=>p.Bind(null)).ToArray();
        if(descriptions.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=descriptions.Length)
            throw new InvalidDataException("Duplicate approved application plugin");
        Content=new(descriptions);
    }
    public GameContent Load(string source,string? preset=null)=>GameRuntime.Load(source,Content,preset);
    public ApplicationCatalog Extend(IEnumerable<ApprovedApplicationPlugin> plugins)=>new(approved.Concat(plugins));
    public Task<RuntimeHost> StartAsync(GameContent content,HostMode mode=HostMode.Headless,RuntimeHost? graphics=null,
        ApplicationClockOptions? clock=null,Action<Action>? dispatch=null)
        // Restore invokes content loaders and waits for their temporary scopes. Keep that
        // work off the caller's UI context, just like runtime activation itself.
        =>Task.Run(()=>StartCore(content,mode,graphics,clock,dispatch));
    private Task<RuntimeHost> StartCore(GameContent content,HostMode mode,RuntimeHost? graphics,
        ApplicationClockOptions? clock,Action<Action>? dispatch)
    {
        // Do not accept a content object validated by another, broader installation catalog.
        var verified=GameRuntime.Restore(content.ExportSnapshot(),Content);
        var selected=verified.Identity.Startup.Plugins.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        var bound=approved.Select((p,i)=>(Factory:p,Description:Content.Plugins[i])).Where(p=>selected.Contains(p.Description.Id)).Select(p=>
        {
            var value=p.Factory.Bind(verified);var description=p.Description;
            if(value.Id!=description.Id || value.Version!=description.Version || value.Api!=description.Api || value.Implementation!=description.Implementation)
                throw new InvalidDataException("Application factory changed its installed identity");
            return value;
        }).ToArray();
        return RuntimeHost.StartScopedAsync(verified.Identity.Startup,bound,[],mode,RuntimeScope.Instance,graphics,clock:clock,dispatch:dispatch);
    }
}
