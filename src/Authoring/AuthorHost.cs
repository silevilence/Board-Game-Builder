using CordiSharp;
using CordiSharp.Registry;

namespace BoardGame.Authoring;

public sealed record AuthorPlugin(string Id, string Version, int Api, string[] Provides, string[] Requires,
    Action<AuthorScope> Activate, string Description, bool Required = false)
{
    public AuthorOperationInfo[] Operations { get; init; } = [];
}

public sealed class AuthorHost : IAsyncDisposable
{
    private readonly Context root = Context.Create();
    private readonly List<PluginHandle> handles = [];
    private readonly SemaphoreSlim lifetime = new(1, 1);
    private bool disposed;
    public EffectiveProfile Profile { get; }
    public IReadOnlyList<AuthorPlugin> Installed { get; }
    public IReadOnlyList<AuthorPlugin> Enabled { get; }
    private AuthorHost(EffectiveProfile profile, IReadOnlyList<AuthorPlugin> installed, IReadOnlyList<AuthorPlugin> enabled)
    { Profile = profile; Installed = installed; Enabled = enabled; }
    public static IReadOnlyList<AuthorPlugin> Builtins =>
    [
        new("author.core", "1", 1, ["author.formats", "author.operations"], [], s =>
        { s.Provide("author.formats", new GameAuthorFormats(false)); s.Provide("author.operations", new AuthorOperations()); }, "Project contract and operation registry", true),
        new("edit.disk", "1", 1, ["author.mode"], [], s => s.Provide("author.mode", s.Own(new DiskEditMode(
            new(s.Profile.Project, s.Profile.Name, "disk")))), "Read current disk content and commit guarded changes"),
        FileOperations.Plugin, FormatOperations.Module, FormatOperations.State, FormatOperations.Configuration, ResourceOperations.Plugin
    ];
    public static IReadOnlyList<AuthorPlugin> Validate(AuthorProfile profile, IReadOnlyList<AuthorPlugin> catalog)
    {
        if (profile.Format != 1 || profile.Plugins == null) throw new InvalidDataException("profile.invalid: expected format 1 and plugins");
        if (catalog.GroupBy(p => p.Id).Any(g => g.Count() != 1)) throw new InvalidDataException("plugin.duplicate: installed ID");
        if (catalog.SelectMany(p => p.Operations).GroupBy(o => o.Id).Any(g => g.Count() != 1)) throw new InvalidDataException("operation.duplicate: installed ID");
        var selected = new Dictionary<string, AuthorPlugin>(StringComparer.Ordinal);
        foreach (var entry in profile.Plugins)
        {
            var plugin = catalog.SingleOrDefault(p => p.Id == entry?.Id) ?? throw new InvalidDataException("plugin.unknown: " + entry?.Id);
            if (entry!.Api != 1 || entry.Api != plugin.Api || entry.Version != plugin.Version) throw new InvalidDataException("plugin.version: " + entry.Id);
            if (!selected.TryAdd(plugin.Id, plugin)) throw new InvalidDataException("plugin.duplicate: " + plugin.Id);
        }
        foreach (var core in catalog.Where(p => p.Required)) selected.TryAdd(core.Id, core);
        if (selected.Values.Count(p => p.Provides.Contains("author.mode")) != 1) throw new InvalidDataException("profile.mode: exactly one edit mode is required");
        var providers = new Dictionary<string, AuthorPlugin>();
        foreach (var plugin in selected.Values)
        {
            if (plugin.Api != 1 || plugin.Requires.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("plugin.invalid: " + plugin.Id);
            foreach (var key in plugin.Provides)
                if (string.IsNullOrWhiteSpace(key) || !providers.TryAdd(key, plugin)) throw new InvalidDataException("plugin.capability: " + key);
        }
        var order = new List<AuthorPlugin>(); var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        foreach (var plugin in selected.Values) Visit(plugin);
        return order;
        void Visit(AuthorPlugin plugin)
        {
            if (visited.Contains(plugin.Id)) return;
            if (!visiting.Add(plugin.Id)) throw new InvalidDataException("plugin.cycle: " + plugin.Id);
            foreach (var key in plugin.Requires) Visit(providers.GetValueOrDefault(key) ?? throw new InvalidDataException("plugin.dependency: " + plugin.Id + " requires " + key));
            visiting.Remove(plugin.Id); visited.Add(plugin.Id); order.Add(plugin);
        }
    }
    public static Task<AuthorHost> Start(EffectiveProfile profile, IReadOnlyList<AuthorPlugin>? catalog = null) => Task.Run(async () =>
    {
        catalog ??= Builtins;
        var host = new AuthorHost(profile, catalog, Validate(profile.Profile, catalog));
        try
        {
            foreach (var plugin in host.Enabled)
            {
                var handle = host.root.Inject(plugin.Requires.Select(k => "author.ready." + k), (context, _) =>
                {
                    var scope = new AuthorScope(host, context, plugin);
                    plugin.Activate(scope);
                    foreach (var key in plugin.Provides)
                    {
                        if (!scope.Published.Contains(key)) throw new InvalidDataException("plugin.service: " + plugin.Id + " did not provide " + key);
                        context.Provide("author.ready." + key, new object());
                    }
                    return Task.CompletedTask;
                });
                host.handles.Add(handle);
                try { await handle.Await().ConfigureAwait(false); }
                catch (Exception error) { throw new InvalidDataException("plugin.initialize: " + plugin.Id + ": " + error.Message, error); }
            }
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    });
    public T Service<T>(string key) where T : class => root.Get<T>(key) ?? throw new InvalidOperationException("plugin.unavailable: " + key);
    public Task<object> Invoke(string id, System.Text.Json.JsonElement arguments, string requestId, CancellationToken cancellation = default) => Execute(async () =>
    {
        var mode = Service<IProjectEditMode>("author.mode");
        var info = Enabled.SelectMany(p => p.Operations).SingleOrDefault(o => o.Id == id)
            ?? throw new AuthorOperationException("operation.unavailable", "Enable the operation's plugin in the selected profile: " + id);
        AuthorOperations.Validate(info, arguments);
        var intent = ProjectView.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { id, arguments, Profile.Project, Profile.Name }));
        if (info.Effects.Contains("project") && mode is IRecoverableEditMode requests)
        {
            var previous = await requests.Find(requestId, intent, cancellation);
            if (previous != null) return previous;
        }
        var view = await mode.Read(cancellation);
        view.Intent = intent;
        return await Service<AuthorOperations>("author.operations").Invoke(id,
            new(view, mode, Service<GameAuthorFormats>("author.formats"), arguments, requestId, cancellation));
    }, cancellation);
    public async Task<T> Execute<T>(Func<Task<T>> action, CancellationToken cancellation = default)
    {
        await lifetime.WaitAsync(cancellation);
        try { ObjectDisposedException.ThrowIf(disposed, this); return await action(); }
        finally { lifetime.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            List<Exception> errors = [];
            foreach (var handle in handles.AsEnumerable().Reverse())
                try { await handle.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            try { await root.Fiber.DisposePluginAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
        finally { lifetime.Release(); }
    }
}

public sealed class AuthorScope
{
    private readonly AuthorHost host;
    private readonly Context context;
    private readonly AuthorPlugin plugin;
    internal HashSet<string> Published { get; } = [];
    public EffectiveProfile Profile => host.Profile;
    internal AuthorScope(AuthorHost host, Context context, AuthorPlugin plugin) { this.host = host; this.context = context; this.plugin = plugin; }
    public void Provide(string key, object value)
    {
        if (!plugin.Provides.Contains(key) || !Published.Add(key)) throw new InvalidOperationException("Undeclared/duplicate capability: " + key);
        context.Provide(key, value);
    }
    public T Service<T>(string key) where T : class
    {
        if (!plugin.Requires.Contains(key)) throw new InvalidOperationException("Undeclared dependency: " + key);
        return host.Service<T>(key);
    }
    public T Own<T>(T value) where T : IDisposable { context.Effect(() => (Action)value.Dispose); return value; }
    public void Register(AuthorOperation operation)
    {
        if (operation.Info.Plugin != plugin.Id || !plugin.Operations.Any(o => o.Id == operation.Info.Id)) throw new InvalidDataException("Undeclared operation: " + operation.Info.Id);
        Own(Service<AuthorOperations>("author.operations").Register(operation));
    }
}
