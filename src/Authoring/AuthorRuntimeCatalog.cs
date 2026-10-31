using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record AuthorRuntimeImplementation(RuntimePluginDefinition Definition,string Origin,PluginManifest? Manifest=null);

/// <summary>Metadata contributions for author inspection; registration never enables a game plugin.</summary>
public sealed class AuthorRuntimeCatalog(PluginStore store)
{
    private readonly Dictionary<string,Func<RuntimeStartup,IReadOnlyList<RuntimePluginDefinition>>> sources=new(StringComparer.Ordinal);
    public event Action? Changed;
    public void Refresh()=>Changed?.Invoke();
    public IDisposable Register(string id,Func<RuntimeStartup,IReadOnlyList<RuntimePluginDefinition>> source)
    {
        if(sources.ContainsKey(id))throw new InvalidOperationException("Duplicate runtime metadata provider: "+id);
        var registration=store.RegisterBuiltins("metadata."+id,source(new(1,"metadata","1",[])));
        sources.Add(id,source);
        Changed?.Invoke();return new AuthorCleanup(()=>{sources.Remove(id);registration.Dispose();Changed?.Invoke();});
    }
    public IReadOnlyList<AuthorRuntimeImplementation> Read(RuntimeStartup startup)
    {
        var selected=startup.Plugins.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        var descriptions=sources.Select(s=>(Id:s.Key,Plugins:s.Value(startup))).ToArray();
        var implementations=descriptions.OrderByDescending(s=>s.Plugins.Any(p=>p.Provides.Contains("definition") && selected.Contains(p.Id)))
            .SelectMany(s=>s.Plugins.Select(p=>new AuthorRuntimeImplementation(p,"内置 · "+s.Id))).ToList();
        foreach(var manifest in store.Installed())
            implementations.Add(new(new(manifest.Id,manifest.Version,manifest.Api,manifest.Provides.Select(p=>p.Name).ToArray(),manifest.Requires.Select(p=>p.Name).ToArray(),
                _=>throw new InvalidOperationException("Metadata cannot activate a runtime implementation"))
                {Implementation=manifest.Fingerprint,Execution=manifest.Execution,Content=new(manifest.ContentFields ?? [])},"显式导入",manifest));
        return implementations.GroupBy(p=>(p.Definition.Id,p.Definition.Version,p.Definition.Api,p.Definition.Implementation)).Select(g=>g.First())
            .OrderBy(p=>p.Definition.Id,StringComparer.Ordinal).ThenBy(p=>p.Definition.Version,StringComparer.Ordinal).ToArray();
    }
}
