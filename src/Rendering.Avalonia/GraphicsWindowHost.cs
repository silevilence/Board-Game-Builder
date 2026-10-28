using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

public sealed record GraphicsConfiguration(RuntimeStartup Startup,IReadOnlyList<RuntimePluginDefinition> Catalog);
public sealed record ViewportComposition(IReadOnlyList<RuntimePluginSelection> Plugins,IReadOnlyList<RuntimePluginDefinition> Catalog);

// One selected backend per top-level window; leases belong to native viewports, not games.
internal sealed class GraphicsWindowHost
{
    private static readonly ConditionalWeakTable<TopLevel,GraphicsWindowHost> hosts=new();
    private readonly TopLevel window;
    private readonly string identity;
    private readonly Task<RenderingHost> graphics;
    internal Task<RuntimeHost> Instance {get;}
    private int references;
    private GraphicsWindowHost(TopLevel window,GraphicsConfiguration configuration)
    {
        this.window=window;identity=JsonSerializer.Serialize(configuration.Startup);
        graphics=RenderingHost.StartAsync(configuration.Startup,configuration.Catalog,Dispatch);
        Instance=StartInstance(configuration);
    }
    internal static void Dispatch(Action action)
    {if(Dispatcher.UIThread.CheckAccess())action();else Dispatcher.UIThread.Invoke(action);}
    private async Task<RuntimeHost> StartInstance(GraphicsConfiguration configuration)
    {
        var host=await graphics.ConfigureAwait(false);
        try {return await RuntimeHost.StartScopedAsync(configuration.Startup,configuration.Catalog,[],HostMode.Graphical,
            RuntimeScope.Instance,host.Runtime).ConfigureAwait(false);}
        catch {await host.DisposeAsync().ConfigureAwait(false);throw;}
    }
    internal static GraphicsWindowHost Acquire(TopLevel window,GraphicsConfiguration configuration)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!hosts.TryGetValue(window,out var owner)) {owner=new(window,configuration);hosts.Add(window,owner);}
        if(owner.identity!=JsonSerializer.Serialize(configuration.Startup)) throw new InvalidOperationException("A graphics window already has a different backend selection; close its viewports before rebuilding");
        owner.references++;return owner;
    }
    internal Task Release()
    {
        Dispatcher.UIThread.VerifyAccess();
        if(--references>0)return Task.CompletedTask;
        hosts.Remove(window);return CloseAsync();
    }
    private async Task CloseAsync()
    {
        try {await (await Instance.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);}
        finally {await (await graphics.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);}
    }
}
