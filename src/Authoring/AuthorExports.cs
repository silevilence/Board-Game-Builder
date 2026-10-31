using System.Text.Json;

namespace BoardGame.Authoring;

public sealed record AuthorExportInfo(string Id,string Plugin,string Version,int Api,string Title,string ArtifactKind,
    string Description,string[] Platforms,string[] Dependencies,AuthorOperationInfo Operation);
public sealed record AuthorExportCheck(ProjectSource Source,string Provider,bool Available,IReadOnlyList<AuthorDiagnostic> Diagnostics)
{public IReadOnlyList<string> Details {get;init;}=[];}
public sealed record AuthorExportRequest(ProjectView View,AuthorRuntimes Runtimes,JsonElement Arguments,
    CancellationToken Cancellation=default,IProgress<string>? Progress=null,Action<string,string>? AdditionalSteps=null,Action<string>? Warning=null);
public interface IAuthorExporter
{
    AuthorExportInfo Info {get;}
    Task<AuthorExportCheck> Preflight(AuthorExportRequest request);
    Task<AuthorArtifact> Export(AuthorExportRequest request);
}

// Scoped registry only: CordiSharp owns provider registration and lifetime.
public sealed class AuthorExports
{
    private sealed class Entry(IAuthorExporter provider) {public IAuthorExporter Provider {get;}=provider;public int Active;}
    private readonly object gate=new();
    private readonly Dictionary<string,Entry> providers=new(StringComparer.Ordinal);
    public event Action? Changed;
    public IReadOnlyList<AuthorExportInfo> Items {get {lock(gate)return providers.Values.Select(p=>p.Provider.Info).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();}}
    public bool Active(string id) {lock(gate)return providers.TryGetValue(id,out var entry) && entry.Active>0;}
    public IDisposable Register(IAuthorExporter provider)
    {
        var info=provider.Info;
        if(string.IsNullOrWhiteSpace(info.Id) || string.IsNullOrWhiteSpace(info.Plugin) || info.Api!=1 || string.IsNullOrWhiteSpace(info.Version) || info.Operation.Plugin!=info.Plugin)
            throw new InvalidDataException("export.identity: invalid provider");
        lock(gate)
        {
            if(providers.ContainsKey(info.Id) || providers.Values.Any(p=>p.Provider.Info.Operation.Id==info.Operation.Id))throw new InvalidDataException("export.duplicate: "+info.Id);
            providers.Add(info.Id,new(provider));
        }
        Changed?.Invoke();return new Registration(this,info.Id);
    }
    private sealed class Registration(AuthorExports owner,string id):IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock(owner.gate)
            {
                if(disposed)return;
                if(owner.providers[id].Active>0)throw new InvalidOperationException("export.active: "+id);
                owner.providers.Remove(id);disposed=true;
            }
            owner.Changed?.Invoke();
        }
    }
    public Task<AuthorExportCheck> Preflight(string id,AuthorExportRequest request)=>Use(id,request,p=>p.Preflight(request));
    public Task<AuthorArtifact> Export(string id,AuthorExportRequest request)=>Use(id,request,async p=>
    {
        var check=await p.Preflight(request).ConfigureAwait(false);
        if(check.Source!=request.View.Source || check.Provider!=id)throw new InvalidDataException("export.source: invalid preflight identity");
        if(!check.Available)throw new AuthorOperationException("export.preflight",string.Join("; ",check.Diagnostics.Select(d=>d.Message)));
        request.Cancellation.ThrowIfCancellationRequested();
        var result=await p.Export(request).ConfigureAwait(false);
        if(result.Source!=request.View.Source || result.Kind!=p.Info.ArtifactKind)throw new InvalidDataException("export.result: provider returned a different source or artifact kind");
        return result;
    });
    private async Task<T> Use<T>(string id,AuthorExportRequest request,Func<IAuthorExporter,Task<T>> action)
    {
        Entry entry;
        lock(gate)
        {
            if(!providers.TryGetValue(id,out entry!))throw new AuthorOperationException("export.unavailable","Enable export provider: "+id);
            entry.Active++;
        }
        try
        {
            request.Cancellation.ThrowIfCancellationRequested();
            AuthorOperations.Validate(entry.Provider.Info.Operation,request.Arguments);
            return await action(entry.Provider).ConfigureAwait(false);
        }
        finally {lock(gate)entry.Active--;}
    }
}
