using BoardGame.Kernel;
using CordiSharp;
using CordiSharp.Registry;

namespace BoardGame.Runtime;

public sealed record RuntimePluginDefinition(string Id,string Version,int Api,string[] Provides,string[] Requires,
    Action<RuntimePluginScope> Activate)
{
    public PluginExecution Execution {get;init;}=new();
    public PluginContentContract? Content {get;init;}
    // Permissions are assigned by the trusted installation catalog, never by game.start.json.
    public string[] Permissions {get;init;}=[];
    public IReadOnlyDictionary<string,string> ServicePermissions {get;init;}=new Dictionary<string,string>();
}

// Validation policy only. CordiSharp owns all service registrations and resource lifetimes.
public sealed class RuntimeHost : IDisposable, IAsyncDisposable
{
    private readonly Context root=Context.Create();
    private readonly List<PluginHandle> handles=[];
    private volatile bool disposed;
    private readonly object shutdown=new();
    private Task? cleanup;
    private readonly Dictionary<string,ApplicationUpdate> updates=new(StringComparer.Ordinal);
    private readonly Dictionary<string,ApplicationRender> renders=new(StringComparer.Ordinal);
    private readonly List<RuntimeHost> children=[];
    private readonly object renderGate=new();
    private readonly RuntimeHost? parent;
    private readonly HostMode mode;
    private readonly RuntimeScope scope;
    private readonly string viewport;
    private readonly Action<Action>? dispatch;
    private readonly TaskCompletionSource initialized=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimeSpan presentationTime;
    private bool rendering;
    private ApplicationFault? renderFault;
    private ApplicationDriver? driver;
    private IReadOnlyList<ScheduledContribution> phases=[];
    private HashSet<string> capabilities=[];
    public IApplicationDriver Driver=>driver ?? throw new InvalidOperationException("Only an instance owns an application driver");
    public ApplicationFault? RenderFault=>renderFault;
    internal bool IsDisposed=>disposed;
    private RuntimeHost(HostMode mode,RuntimeScope scope,RuntimeHost? parent,string viewport,Action<Action>? dispatch)
    {this.mode=mode;this.scope=scope;this.parent=parent;this.viewport=viewport;this.dispatch=dispatch;}
    public static IReadOnlyList<RuntimePluginDefinition> Validate(RuntimeStartup startup,
        IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required,HostMode mode=HostMode.Headless)
    {
        if(startup.Format!=1 || string.IsNullOrWhiteSpace(startup.Id) || string.IsNullOrWhiteSpace(startup.Version) || startup.Plugins==null)
            throw new InvalidDataException("game.start.json: invalid identity or unsupported format");
        var selected=new Dictionary<string,RuntimePluginDefinition>(StringComparer.Ordinal);
        var providers=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var selection in startup.Plugins)
        {
            if(selection==null || string.IsNullOrWhiteSpace(selection.Id)) throw new InvalidDataException("Invalid runtime plugin identity");
            var matches=catalog.Where(p=>p.Id==selection.Id).ToArray();
            if(matches.Length!=1) throw new InvalidDataException(selection.Id+": unknown or unapproved runtime implementation");
            var plugin=matches[0];
            if(plugin.Provides==null || plugin.Requires==null || plugin.Activate==null ||
                plugin.Requires.Any(string.IsNullOrWhiteSpace) || plugin.Requires.Distinct().Count()!=plugin.Requires.Length)
                throw new InvalidDataException(plugin.Id+": invalid capability declaration");
            if(plugin.Api!=1 || selection.Api!=plugin.Api || selection.Version!=plugin.Version)
                throw new InvalidDataException(selection.Id+": incompatible plugin API/version");
            if(!selected.TryAdd(plugin.Id,plugin)) throw new InvalidDataException(selection.Id+": duplicate plugin identity");
            if(selection.Configuration.ValueKind!=System.Text.Json.JsonValueKind.Object)
                throw new InvalidDataException(selection.Id+": configuration must be an object");
            if(plugin.Execution==null || plugin.Execution.Modes==0 || (plugin.Execution.Modes & ~HostMode.All)!=0 || !Enum.IsDefined(plugin.Execution.Scope))
                throw new InvalidDataException(plugin.Id+": invalid execution declaration");
            if(plugin.Permissions==null || plugin.Permissions.Any(string.IsNullOrWhiteSpace) || plugin.ServicePermissions==null ||
                plugin.ServicePermissions.Any(p=>!plugin.Provides.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
                throw new InvalidDataException(plugin.Id+": invalid service permission declaration");
        }
        if(mode is not (HostMode.Headless or HostMode.Graphical)) throw new ArgumentOutOfRangeException(nameof(mode));
        selected=selected.Where(p=>(p.Value.Execution.Modes & mode)!=0 && (mode!=HostMode.Headless || p.Value.Execution.Scope==RuntimeScope.Instance))
            .ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
        foreach(var plugin in selected.Values)
        {
            foreach(var capability in plugin.Provides)
                if(string.IsNullOrWhiteSpace(capability) || !providers.TryAdd(capability,plugin.Id))
                    throw new InvalidDataException(plugin.Id+": duplicate/invalid capability "+capability);
        }
        foreach(var capability in required)
            if(!providers.ContainsKey(capability)) throw new InvalidDataException("Missing required capability: "+capability);
        _=PhaseOrder.Sort(selected.Values.SelectMany(p=>(p.Execution.Contributions ?? []).Select(c=>new ScheduledContribution(p.Id,p.Execution.Scope,c))));
        var order=new List<RuntimePluginDefinition>();var visiting=new HashSet<string>();var done=new HashSet<string>();
        foreach(var id in selected.Keys.Order(StringComparer.Ordinal)) Visit(id);
        return order;
        void Visit(string id)
        {
            if(done.Contains(id)) return;
            if(!visiting.Add(id)) throw new InvalidDataException(id+": runtime dependency cycle");
            foreach(var dependency in selected[id].Requires)
            {
                if(!providers.TryGetValue(dependency,out var provider)) throw new InvalidDataException(id+": missing capability "+dependency);
                if(selected[provider].Execution.Scope>selected[id].Execution.Scope)
                    throw new InvalidDataException(id+": dependency on shorter-lived scope "+dependency);
                if(selected[provider].ServicePermissions.TryGetValue(dependency,out var permission) && !selected[id].Permissions.Contains(permission,StringComparer.Ordinal))
                    throw new InvalidDataException(id+": capability permission denied: "+dependency);
                Visit(provider);
            }
            visiting.Remove(id);done.Add(id);order.Add(selected[id]);
        }
    }
    public static Task<RuntimeHost> StartAsync(RuntimeStartup startup,
        IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required)
        =>StartScopedAsync(startup,catalog,required);
    public static Task<RuntimeHost> StartScopedAsync(RuntimeStartup startup,
        IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required,HostMode mode=HostMode.Headless,
        RuntimeScope scope=RuntimeScope.Instance,RuntimeHost? parent=null,string viewport="default",ApplicationClockOptions? clock=null,Action<Action>? dispatch=null)
    {
        // Runtime fibers must never capture an editor/UI synchronization context.
        // Freeze inputs before scheduling, so the graph cannot change in the queue.
        startup=startup with {Plugins=startup.Plugins.Select(p=>p with {Configuration=p.Configuration.Clone()}).ToList()};
        catalog=catalog.Select(p=>p with {Provides=p.Provides?.ToArray()!,Requires=p.Requires?.ToArray()!,
            Permissions=p.Permissions?.ToArray()!,ServicePermissions=p.ServicePermissions?.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal)!,
            Execution=p.Execution with {Contributions=p.Execution.Contributions?.Select(c=>c with {Before=c.Before?.ToArray(),After=c.After?.ToArray()}).ToArray()}}).ToArray();
        var requiredCopy=required.ToArray();
        clock ??=new();clock.Validate();
        return Task.Run(()=>StartCore(startup,catalog,requiredCopy,mode,scope,parent,viewport,clock,dispatch));
    }
    private static async Task<RuntimeHost> StartCore(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required,
        HostMode mode,RuntimeScope scope,RuntimeHost? parent,string viewport,ApplicationClockOptions clock,Action<Action>? dispatch)
    {
        var graph=Validate(startup,catalog,required,mode);
        if(!Enum.IsDefined(scope) || (mode==HostMode.Headless && scope!=RuntimeScope.Instance) ||
            (parent!=null && (parent.mode!=mode || parent.scope>=scope)) || (scope==RuntimeScope.Viewport && parent?.scope!=RuntimeScope.Instance))
            throw new InvalidDataException("Invalid runtime scope parent/mode");
        var order=graph.Where(p=>p.Execution.Scope==scope).ToArray();
        var host=new RuntimeHost(mode,scope,parent,viewport,dispatch);
        host.phases=PhaseOrder.Sort(graph.SelectMany(p=>(p.Execution.Contributions ?? []).Select(c=>new ScheduledContribution(p.Id,p.Execution.Scope,c))))
            .Where(p=>p.Scope==scope).ToArray();
        host.capabilities=order.SelectMany(p=>p.Provides).ToHashSet(StringComparer.Ordinal);
        // Dependencies crossing scope boundaries must already exist in the explicit parent.
        var inherited=order.SelectMany(p=>p.Requires).Except(host.capabilities).Distinct().ToArray();
        try
        {
            if(parent!=null)
                lock(parent.shutdown)
                {
                    ObjectDisposedException.ThrowIf(parent.disposed,parent);
                    parent.children.Add(host);
                }
            foreach(var key in inherited)
                if(parent==null || !parent.HasCapability(key)) throw new InvalidDataException("Missing parent capability: "+key);
            foreach(var key in host.capabilities)
                if(parent?.HasCapability(key)==true) throw new InvalidDataException("Duplicate parent capability: "+key);
            foreach(var key in inherited) host.root.Provide(Key(key),new object());
            foreach(var plugin in order)
            {
                var selection=startup.Plugins.Single(p=>p.Id==plugin.Id);
                var handle=host.root.Inject(plugin.Requires.Select(Key),(context,_)=>ActivateAsync(context));
                async Task ActivateAsync(Context context)
                {
                    var scope=new RuntimePluginScope(host,context,plugin,selection.Configuration);
                    await host.DispatchAsync(()=>plugin.Activate(scope));
                    foreach(var capability in plugin.Provides)
                    {
                        if(!scope.Published.Contains(capability)) throw new InvalidDataException(plugin.Id+": did not provide "+capability);
                        context.Provide(Key(capability),new object());
                    }
                    foreach(var contribution in plugin.Execution.Contributions ?? [])
                        if(!(contribution.Phase==ApplicationPhase.Update?host.updates.ContainsKey(plugin.Id+"/"+contribution.Id):host.renders.ContainsKey(plugin.Id+"/"+contribution.Id)))
                            throw new InvalidDataException(plugin.Id+": did not register "+contribution.Id);
                }
                host.handles.Add(handle);
                try {await handle.Await().ConfigureAwait(false);}
                catch(Exception error) {throw new InvalidDataException(plugin.Id+": runtime activation failed: "+error.Message,error);}
            }
            if(scope==RuntimeScope.Instance) host.driver=new(host.phases.Where(p=>p.Declaration.Phase==ApplicationPhase.Update)
                .Select(p=>(p,host.updates[p.Key])).ToArray(),clock);
            ObjectDisposedException.ThrowIf(host.disposed,host);
            host.initialized.TrySetResult();
            return host;
        }
        catch(Exception activation)
        {
            host.initialized.TrySetResult();
            try {await host.DisposeAsync().ConfigureAwait(false);}
            catch(Exception cleanupError) {throw new AggregateException("Runtime activation and cleanup failed",activation,cleanupError);}
            throw;
        }
    }
    public T Service<T>(string key) where T:class
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        return root.Get<T>(key) ?? parent?.Service<T>(key) ?? throw new InvalidOperationException("Unavailable runtime capability: "+key);
    }
    private bool HasCapability(string key)=>!disposed && (capabilities.Contains(key) || parent?.HasCapability(key)==true);
    // CordiSharp roots share a pump. A synchronous UI dispatch on that pump would block
    // unrelated content loads when the UI is awaiting another root's initialization.
    private Task DispatchAsync(Action action)
    {
        if(dispatch!=null)return Task.Run(()=>dispatch(action));
        action();return Task.CompletedTask;
    }
    internal void Release(IDisposable resource) {if(dispatch!=null)dispatch(resource.Dispose);else resource.Dispose();}
    internal Task ReleaseAsync(IDisposable resource)=>DispatchAsync(resource.Dispose);
    internal Action Register(string key,ApplicationUpdate? update,ApplicationRender? render)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(update!=null ? !updates.TryAdd(key,update) : !renders.TryAdd(key,render!)) throw new InvalidOperationException(key+": duplicate callback");
        return ()=>{updates.Remove(key);renders.Remove(key);};
    }
    public void Render(TimeSpan elapsed)
    {
        if(scope!=RuntimeScope.Viewport) throw new InvalidOperationException("Render requires a viewport scope");
        if(elapsed<TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        lock(renderGate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(rendering) throw new InvalidOperationException("Render reentry");
            if(renderFault!=null) throw new InvalidOperationException("Viewport faulted",renderFault.Error);
            rendering=true;
            try
            {
                parent!.driver!.Present((tick,alpha)=>
                {
                    presentationTime+=elapsed;
                    var frame=new RenderFrame(viewport,presentationTime,elapsed,tick,alpha);
                    foreach(var phase in phases)
                        try {renders[phase.Key](frame);}
                        catch(Exception error) {renderFault=new(phase.Plugin,phase.Declaration.Id,ApplicationPhase.Render,error);throw;}
                });
            }
            finally {rendering=false;}
        }
    }
    private static string Key(string capability)=>"runtime.capability."+capability;
    public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync()
    {
        lock(shutdown)
        {
            disposed=true;
            return new(cleanup ??= Task.Run(DisposeCore));
        }
    }
    private async Task DisposeCore()
    {
        await initialized.Task.ConfigureAwait(false);
        List<Exception> failures=[];
        if(driver!=null) try {await driver.DisposeAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
        RuntimeHost[] owned;
        lock(shutdown) owned=children.ToArray();
        foreach(var child in owned.Reverse())
            try {await child.DisposeAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
        lock(renderGate) { } // Wait for a synchronous render before releasing its resources.
        foreach(var handle in handles.AsEnumerable().Reverse())
            try {await handle.DisposeAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
        handles.Clear();
        try {await root.Fiber.DisposePluginAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
        if(parent!=null) lock(parent.shutdown) parent.children.Remove(this);
        if(failures.Count>0) throw new AggregateException("Runtime cleanup failed",failures);
    }
}

public sealed class RuntimePluginScope
{
    private readonly RuntimeHost host;
    private readonly Context context;
    private readonly RuntimePluginDefinition definition;
    internal HashSet<string> Published {get;}=[];
    public System.Text.Json.JsonElement Configuration {get;}
    internal RuntimePluginScope(RuntimeHost host,Context context,RuntimePluginDefinition definition,System.Text.Json.JsonElement configuration)
    {this.host=host;this.context=context;this.definition=definition;Configuration=configuration;}
    public void Provide(string key,object service)
    {
        ObjectDisposedException.ThrowIf(host.IsDisposed,this);
        if(!definition.Provides.Contains(key) || !Published.Add(key)) throw new InvalidOperationException(definition.Id+": undeclared/duplicate service "+key);
        context.Provide(key,service);
    }
    public T Service<T>(string key) where T:class
    {
        if(!definition.Requires.Contains(key)) throw new InvalidOperationException(definition.Id+": undeclared dependency "+key);
        return host.Service<T>(key);
    }
    public T Own<T>(T resource) where T:IDisposable
    {
        if(host.IsDisposed) {host.Release(resource);throw new ObjectDisposedException(nameof(RuntimePluginScope));}
        context.Effect(()=>(Func<Task>)(()=>host.ReleaseAsync(resource)));return resource;
    }
    public void Update(string id,ApplicationUpdate callback)=>Register(id,ApplicationPhase.Update,callback,null);
    public void Render(string id,ApplicationRender callback)=>Register(id,ApplicationPhase.Render,null,callback);
    private void Register(string id,ApplicationPhase phase,ApplicationUpdate? update,ApplicationRender? render)
    {
        if(!(definition.Execution.Contributions ?? []).Any(c=>c.Id==id && c.Phase==phase))
            throw new InvalidOperationException(definition.Id+": undeclared callback "+id);
        context.Effect(()=>host.Register(definition.Id+"/"+id,update,render));
    }
}
