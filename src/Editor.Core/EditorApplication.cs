using CordiSharp;
using CordiSharp.Events;
using CordiSharp.Registry;

namespace BoardGame.Editor;

public sealed record EditorPluginDefinition(string Id, int Api, string[] Provides, string[] Requires,
    Action<EditorPluginScope> Activate, bool Required=false)
{
    public string Version { get; init; } = "1";
    public Func<EditorPluginScope,Task>? InitializeAsync { get; init; }
}
public enum EditorPluginStatus { Disabled, Waiting, Enabled, Failed }
public sealed record EditorPluginState(string Id, EditorPluginStatus Status, string Reason);

// Policy and diagnostics around CordiSharp fibers, not a second effect/service runtime.
public sealed class EditorApplication : IAsyncDisposable
{
    private readonly Dictionary<string,EditorPluginDefinition> definitions=new(StringComparer.Ordinal);
    private readonly Dictionary<string,PluginHandle> handles=new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,EditorPluginScope> scopes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,EditorPluginState> states=new(StringComparer.Ordinal);
    private readonly Dictionary<string,List<Func<string?>>> guards=new(StringComparer.Ordinal);
    private readonly SemaphoreSlim lifecycle=new(1,1);
    private readonly Action<Action> dispatch;
    private readonly Func<Action,Task> dispatchAsync;
    private bool disposed;
    private readonly List<Func<Task<bool>>> shutdownChecks=[];
    public IDisposable BeforeShutdown(Func<Task<bool>> check) {shutdownChecks.Add(check);return new Cleanup(()=>shutdownChecks.Remove(check));}
    public async Task<bool> RequestShutdown()
    {
        foreach(var check in shutdownChecks.ToArray()) if(!await check()) return false;
        await DisposeAsync();return true;
    }
    public Context Root { get; }=Context.Create();
    public IReadOnlyList<EditorPluginDefinition> Catalog=>definitions.Values.ToArray();
    public IReadOnlyList<EditorPluginState> States=>states.Values.ToArray();
    public event Action? Changed;
    public List<string> Diagnostics { get; }=[];
    public EditorApplication(Action<Action>? dispatch=null):this(dispatch,null) { }
    public EditorApplication(Action<Action>? dispatch,Func<Action,Task>? dispatchAsync)
    {
        this.dispatch=dispatch ?? (action=>action());
        this.dispatchAsync=dispatchAsync ?? (dispatch==null?action=>{action();return Task.CompletedTask;}:action=>Task.Run(()=>dispatch(action)));
    }
    internal void OnOwnerThread(Action action)=>dispatch(action);
    internal Task OnOwnerThreadAsync(Action action)=>dispatchAsync(action);
    public T Service<T>(string key) where T:class => Root.Get<T>(key) ?? throw new InvalidOperationException("服务未启用："+key);
    public void Install(EditorPluginDefinition plugin)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(string.IsNullOrWhiteSpace(plugin.Id) || string.IsNullOrWhiteSpace(plugin.Version) || plugin.Api!=1 || plugin.Provides==null || plugin.Requires==null || plugin.Provides.Concat(plugin.Requires).Any(string.IsNullOrWhiteSpace)
            || plugin.Provides.Distinct().Count()!=plugin.Provides.Length) throw new InvalidDataException("Invalid plugin identity/API: "+plugin.Id);
        if(definitions.Values.Any(p=>p.Provides.Intersect(plugin.Provides).Any())) throw new InvalidDataException("Duplicate capability: "+plugin.Id);
        if(!definitions.TryAdd(plugin.Id,plugin)) throw new InvalidDataException("Duplicate plugin: "+plugin.Id);
        states[plugin.Id]=new(plugin.Id,EditorPluginStatus.Disabled,"");
    }
    public async Task Enable(string id)
    {
        await lifecycle.WaitAsync();
        try { ObjectDisposedException.ThrowIf(disposed,this); await EnableCore(id); }
        finally { lifecycle.Release(); Notify(); }
    }
    private async Task EnableCore(string id)
    {
        var added=new List<string>();
        try
        {
            foreach(var next in Order([id])) if(!handles.ContainsKey(next))
            {
                var definition=definitions[next];
                states[next]=new(next,EditorPluginStatus.Waiting,"等待依赖与初始化");
                var handle=Root.Inject(definition.Requires.Select(Capability),(ctx,_)=>Activate(ctx));
                async Task Activate(Context ctx)
                {
                    var scope=new EditorPluginScope(this,ctx,definition);scopes[next]=scope;
                    Task? initialization=null;
                    await OnOwnerThreadAsync(()=>
                    {
                        definition.Activate(scope);
                        initialization=definition.InitializeAsync?.Invoke(scope);
                    });
                    if(initialization!=null) await initialization.WaitAsync(scope.Cancellation);
                    scope.Cancellation.ThrowIfCancellationRequested();
                    foreach(var capability in definition.Provides) ctx.Provide(Capability(capability),new object());
                }
                handles.Add(next,handle); added.Add(next);
                try { await handle.Await().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch(Exception error) { states[next]=new(next,EditorPluginStatus.Failed,error.Message); throw; }
                states[next]=new(next,EditorPluginStatus.Enabled,"");
            }
        }
        catch(Exception error)
        {
            foreach(var next in added.AsEnumerable().Reverse()) await Remove(next);
            if(definitions.ContainsKey(id)) states[id]=new(id,error.Message.Contains("Missing capability")?EditorPluginStatus.Waiting:EditorPluginStatus.Failed,error.Message);
            Diagnostics.Add(id+": "+error.Message);
            throw;
        }
    }
    public async Task Disable(string id) => await Change(id,false);
    public async Task Reload(string id) => await Change(id,true);
    public async Task Uninstall(string id)=>await Change(id,false,true);
    public IDisposable Retain(string id,string reason)=>Guard(id,()=>reason);
    private async Task Change(string id,bool reload,bool uninstall=false)
    {
        await lifecycle.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            var affected=Dependents(id); var order=Order(affected.Where(handles.ContainsKey)).Where(affected.Contains).ToArray();
            foreach(var next in order)
            {
                var reason=BlockedReason(next);
                if(reason!=null) {states[next]=states[next] with {Reason=reason};throw new InvalidOperationException(next+": "+reason);}
            }
            foreach(var next in order.Reverse()) await Remove(next);
            if(uninstall) {definitions.Remove(id);states.Remove(id);}
            if(reload)
            {
                try { foreach(var next in order) await EnableCore(next);if(!order.Contains(id)) await EnableCore(id); }
                catch {foreach(var next in order.Reverse()) await Remove(next);throw;}
            }
        }
        finally { lifecycle.Release(); Notify(); }
    }
    public string? BlockedReason(string id)
    {
        if(definitions[id].Required) return "必需插件，仅随应用退出释放";
        return guards.GetValueOrDefault(id)?.Select(g=>g()).FirstOrDefault(r=>r!=null);
    }
    internal IDisposable Guard(string id,Func<string?> guard)
    {
        if(!guards.TryGetValue(id,out var list)) guards[id]=list=[];
        list.Add(guard);
        return new Cleanup(()=> {list.Remove(guard);if(list.Count==0) guards.Remove(id);});
    }
    private async Task Remove(string id)
    {
        if(scopes.TryRemove(id,out var scope)) scope.Cancel();
        if(handles.Remove(id,out var handle)) await handle.DisposeAsync();
        if(states[id].Status!=EditorPluginStatus.Failed) states[id]=new(id,EditorPluginStatus.Disabled,"");
    }
    private HashSet<string> Dependents(string id)
    {
        if(!definitions.ContainsKey(id)) throw new InvalidDataException("Unknown plugin: "+id);
        var affected=new HashSet<string> {id};
        bool changed;
        do
        {
            changed=false;
            var capabilities=affected.SelectMany(i=>definitions[i].Provides).ToHashSet();
            foreach(var next in handles.Keys) if(definitions[next].Requires.Any(capabilities.Contains)) changed|=affected.Add(next);
        } while(changed);
        return affected;
    }
    private List<string> Order(IEnumerable<string> roots)
    {
        var result=new List<string>();var visiting=new HashSet<string>();var done=new HashSet<string>();
        foreach(var id in roots.Order(StringComparer.Ordinal)) Visit(id);
        return result;
        void Visit(string id)
        {
            if(done.Contains(id)) return;
            if(!definitions.TryGetValue(id,out var plugin)) throw new InvalidDataException("Unknown plugin: "+id);
            if(!visiting.Add(id)) throw new InvalidDataException("Plugin dependency cycle: "+string.Join(" → ",visiting)+" → "+id);
            foreach(var key in plugin.Requires)
            {
                var provider=definitions.Values.SingleOrDefault(p=>p.Provides.Contains(key));
                if(provider==null) throw new InvalidDataException(id+": Missing capability "+key);
                Visit(provider.Id);
            }
            visiting.Remove(id);done.Add(id);result.Add(id);
        }
    }
    internal static string Capability(string key)=>"editor.capability."+key;
    private void Notify()=>OnOwnerThread(()=>Changed?.Invoke());
    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if(disposed) return; disposed=true;
            foreach(var id in Order(handles.Keys).AsEnumerable().Reverse()) await Remove(id);
            await Root.Fiber.DisposePluginAsync();
            Changed=null;guards.Clear();definitions.Clear();
        }
        finally {lifecycle.Release();}
    }
}

public sealed class Cleanup(Action action) : IDisposable
{
    private Action? action=action;
    public void Dispose()=>Interlocked.Exchange(ref action,null)?.Invoke();
}

public sealed class EditorPluginScope
{
    private readonly EditorApplication application;
    private readonly EditorPluginDefinition definition;
    public Context Context { get; }
    public string Id=>definition.Id;
    public CancellationToken Cancellation { get; }
    private readonly CancellationTokenSource cancellation;
    private bool requiresStructuredFormat;
    private Func<bool>? formatAvailable;
    private long? documentGeneration;
    internal EditorPluginScope(EditorApplication application,Context context,EditorPluginDefinition definition,CancellationToken parent=default)
    {
        this.application=application;Context=context;this.definition=definition;
        cancellation=CancellationTokenSource.CreateLinkedTokenSource(parent);
        Cancellation=cancellation.Token;
        Own(new Cleanup(()=> {Cancel();cancellation.Dispose();}));
    }
    internal void Cancel()
    {
        if(Cancellation.IsCancellationRequested) return;
        try {cancellation.Cancel();} catch(Exception e) {application.Diagnostics.Add(Id+": cancellation: "+e.Message);}
    }
    public void Own(IDisposable resource)
    {
        if(Cancellation.IsCancellationRequested) {application.OnOwnerThread(resource.Dispose);Cancellation.ThrowIfCancellationRequested();}
        // Never block CordiSharp's pump on a UI thread that may be loading a runtime snapshot.
        Context.Effect(()=> (Func<Task>)(()=>application.OnOwnerThreadAsync(resource.Dispose)));
    }
    public void Guard(Func<string?> reason)=>Own(application.Guard(Id,reason));
    public void OwnAsync(IAsyncDisposable resource)
    {
        Context.Effect(()=>(Func<Task>)(async()=>
        {
            Task pending=Task.CompletedTask;
            await application.OnOwnerThreadAsync(()=>pending=resource.DisposeAsync().AsTask());
            await pending;
        }));
    }
    public void Provide(string key,object service)
    {
        Cancellation.ThrowIfCancellationRequested();
        if(!definition.Provides.Contains(key)) throw new InvalidOperationException("Undeclared service: "+key);
        Context.Provide(key,service);
    }
    public T Service<T>(string key) where T:class
    {
        Cancellation.ThrowIfCancellationRequested();
        if(!definition.Requires.Contains(key) && !definition.Provides.Contains(key)) throw new InvalidOperationException("Undeclared dependency: "+key);
        return application.Service<T>(key);
    }
    public void On<T>(EventKey<T> key,Action<T> handler)
    {
        Cancellation.ThrowIfCancellationRequested();
        Context.On(key,(_,value)=> {if(!Cancellation.IsCancellationRequested) application.OnOwnerThread(()=>handler(value));return null;});
    }
    public void Emit<T>(EventKey<T> key,T value) {Cancellation.ThrowIfCancellationRequested();Context.Emit(key,value);}
    public EditorPluginScope ForContext(Context context,bool requiresStructuredFormat=false)=>ForContext(context,requiresStructuredFormat,null);
    public EditorPluginScope ForContext(Context context,bool requiresStructuredFormat,Func<bool>? formatAvailable)
    {Cancellation.ThrowIfCancellationRequested();return new(application,context,definition,Cancellation) {requiresStructuredFormat=requiresStructuredFormat,formatAvailable=formatAvailable};}
    public EditorPluginScope ForDocumentContext(Context context,bool requiresStructuredFormat,Func<bool>? formatAvailable)
    {
        var child=ForContext(context,requiresStructuredFormat,formatAvailable);
        child.documentGeneration=Service<DocumentWorkspace>("documents").Generation;
        return child;
    }
    private bool CurrentGeneration=>documentGeneration==null || Service<DocumentWorkspace>("documents").Generation==documentGeneration;
    public ModuleDocument? Document
    {
        get {var document=Service<DocumentWorkspace>("documents").Current;return !CurrentGeneration || requiresStructuredFormat && document?.SupportsStructuredEditing!=true || formatAvailable?.Invoke()==false?null:document;}
    }
    public void OnDocumentChanged(Action action)
    {
        var documents=Service<DocumentWorkspace>("documents");
        void Refresh() {if(!Cancellation.IsCancellationRequested && CurrentGeneration && formatAvailable?.Invoke()!=false && (!requiresStructuredFormat || documents.Current?.SupportsStructuredEditing==true)) action();}
        documents.Changed+=Refresh;
        Own(new Cleanup(()=>documents.Changed-=Refresh));
    }
}
