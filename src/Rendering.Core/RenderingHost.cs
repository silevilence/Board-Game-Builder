using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

public static class RenderingPlugin
{
    public const string Capability="render.backend";
    public static RuntimePluginDefinition Backend(RenderBackendDescriptor descriptor,Func<IRenderBackend> create)=>
        new(descriptor.Id,descriptor.Version,1,[Capability],[],scope=>
        {
            var backend=create();
            if(backend.Descriptor!=descriptor)
            {
                backend.Dispose();
                throw new InvalidDataException("Render backend identity differs from approved descriptor");
            }
            scope.Provide(Capability,scope.Own(new RenderBackend(backend)));
        }) {Execution=new(HostMode.Graphical,RuntimeScope.GraphicsHost)};
}

/// <summary>One immutable backend selection per graphics host. Rebuild the host to switch.
/// RuntimeHost owns instance/viewport plugin roots; the backend owns device surfaces.</summary>
public sealed class RenderingHost : IAsyncDisposable
{
    public RuntimeHost Runtime {get;}
    public RenderBackend Backend=>Runtime.Service<RenderBackend>(RenderingPlugin.Capability);
    private RenderingHost(RuntimeHost runtime)=>Runtime=runtime;
    public static async Task<RenderingHost> StartAsync(RuntimeStartup startup,IReadOnlyList<RuntimePluginDefinition> catalog,Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var graph=RuntimeHost.Validate(startup,catalog,[RenderingPlugin.Capability],HostMode.Graphical);
        var backend=graph.Single(p=>p.Provides.Contains(RenderingPlugin.Capability));
        if(backend.Execution.Scope!=RuntimeScope.GraphicsHost || backend.Execution.Modes!=HostMode.Graphical)
            throw new InvalidDataException("Render backend requires graphical host scope");
        return new(await RuntimeHost.StartScopedAsync(startup,catalog,[RenderingPlugin.Capability],HostMode.Graphical,
            RuntimeScope.GraphicsHost,dispatch:dispatch).ConfigureAwait(false));
    }
    public ValueTask DisposeAsync()=>Runtime.DisposeAsync();
}
