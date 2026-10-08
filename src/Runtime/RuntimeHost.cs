using BoardGame.Kernel;
using CordiSharp;
using CordiSharp.Registry;

namespace BoardGame.Runtime;

public sealed record RuntimePluginDefinition(string Id,string Version,int Api,string[] Provides,string[] Requires,
    Action<RuntimePluginScope> Activate);

// Validation policy only. CordiSharp owns all service registrations and resource lifetimes.
public sealed class RuntimeHost : IDisposable, IAsyncDisposable
{
    private readonly Context root=Context.Create();
    private readonly List<PluginHandle> handles=[];
    private volatile bool disposed;
    private readonly object shutdown=new();
    private Task? cleanup;
    internal bool IsDisposed=>disposed;
    private RuntimeHost() { }
    public static IReadOnlyList<RuntimePluginDefinition> Validate(RuntimeStartup startup,
        IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required)
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
            foreach(var capability in plugin.Provides)
                if(string.IsNullOrWhiteSpace(capability) || !providers.TryAdd(capability,plugin.Id))
                    throw new InvalidDataException(plugin.Id+": duplicate/invalid capability "+capability);
        }
        foreach(var capability in required)
            if(!providers.ContainsKey(capability)) throw new InvalidDataException("Missing required capability: "+capability);
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
                Visit(provider);
            }
            visiting.Remove(id);done.Add(id);order.Add(selected[id]);
        }
    }
    public static Task<RuntimeHost> StartAsync(RuntimeStartup startup,
        IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required)
    {
        // Runtime fibers must never capture an editor/UI synchronization context.
        // Freeze inputs before scheduling, so the graph cannot change in the queue.
        startup=Wire.Copy(startup);
        catalog=catalog.Select(p=>p with {Provides=p.Provides?.ToArray()!,Requires=p.Requires?.ToArray()!}).ToArray();
        var requiredCopy=required.ToArray();
        return Task.Run(()=>StartCore(startup,catalog,requiredCopy));
    }
    private static async Task<RuntimeHost> StartCore(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog,IEnumerable<string> required)
    {
        var order=Validate(startup,catalog,required);
        var host=new RuntimeHost();
        try
        {
            foreach(var plugin in order)
            {
                var selection=startup.Plugins.Single(p=>p.Id==plugin.Id);
                var handle=host.root.Inject(plugin.Requires.Select(Key),(context,_)=>
                {
                    var scope=new RuntimePluginScope(host,context,plugin,selection.Configuration);
                    plugin.Activate(scope);
                    foreach(var capability in plugin.Provides)
                    {
                        if(!scope.Published.Contains(capability)) throw new InvalidDataException(plugin.Id+": did not provide "+capability);
                        context.Provide(Key(capability),new object());
                    }
                    return Task.CompletedTask;
                });
                host.handles.Add(handle);
                try {await handle.Await().ConfigureAwait(false);}
                catch(Exception error) {throw new InvalidDataException(plugin.Id+": runtime activation failed: "+error.Message,error);}
            }
            return host;
        }
        catch(Exception activation)
        {
            try {await host.DisposeAsync().ConfigureAwait(false);}
            catch(Exception cleanupError) {throw new AggregateException("Runtime activation and cleanup failed",activation,cleanupError);}
            throw;
        }
    }
    public T Service<T>(string key) where T:class
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        return root.Get<T>(key) ?? throw new InvalidOperationException("Unavailable runtime capability: "+key);
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
        List<Exception> failures=[];
        foreach(var handle in handles.AsEnumerable().Reverse())
            try {await handle.DisposeAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
        handles.Clear();
        try {await root.Fiber.DisposePluginAsync().ConfigureAwait(false);} catch(Exception error) {failures.Add(error);}
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
        if(host.IsDisposed) {resource.Dispose();throw new ObjectDisposedException(nameof(RuntimePluginScope));}
        context.Effect(()=>(Action)resource.Dispose);return resource;
    }
}
