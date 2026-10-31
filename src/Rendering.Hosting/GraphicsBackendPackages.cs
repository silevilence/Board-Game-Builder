using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

/// <summary>Ordinary hosts isolate backend-specific managed dependencies. The package path is host-owned.</summary>
public sealed class GraphicsBackendPackages(Func<RuntimePluginDefinition> windowsDx,string desktopGlPackage)
{
    public GraphicsBackendLease Open(RuntimeStartup startup)
    {
        if(!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)
            throw new PlatformNotSupportedException("Graphics distribution supports win-x64 only");
        var descriptions=GraphicsBackendContract.Descriptions;
        var selected=startup.Plugins.Where(p=>descriptions.Any(d=>d.Id==p.Id)).ToArray();
        if(selected.Length!=1)throw new InvalidDataException("Choose exactly one installed graphics backend (render.windowsdx or render.desktopgl)");
        var selection=selected[0];var header=new RuntimeStartup(1,"graphics","1",[selection]);
        RuntimeHost.Validate(header,descriptions,[RenderingPlugin.Capability],HostMode.Graphical);
        RuntimeDiscovery.CheckObject(selection.Configuration,[],selection.Id+".configuration");
        if(selection.Id=="render.windowsdx")return new(header,windowsDx(),null);
        if(!File.Exists(Path.Combine(desktopGlPackage,PluginManifest.FileName)))throw new FileNotFoundException("DesktopGL host package is not installed; build and deploy the pinned backend package",desktopGlPackage);
        var store=new PluginStore(desktopGlPackage,"win-x64",[typeof(RenderBackend).Assembly,typeof(IDraw2D).Assembly,typeof(BoardGame.Resources.AssetSet).Assembly]);
        var lease=store.AcquirePackage(desktopGlPackage,selection.Id);
        try
        {
            var definition=lease.Plugin.Bind(null) with {Implementation=null};
            if(definition.Version!=selection.Version || definition.Api!=selection.Api || !definition.Provides.SequenceEqual([RenderingPlugin.Capability]) || definition.Execution.Scope!=RuntimeScope.GraphicsHost)
                throw new InvalidDataException("DesktopGL distribution contract mismatch");
            return new(header,definition,lease);
        }
        catch{lease.Dispose();throw;}
    }
}

public sealed class GraphicsBackendLease(RuntimeStartup startup,RuntimePluginDefinition definition,IDisposable? owner):IDisposable
{
    public RuntimeStartup Startup {get;}=startup;
    public RuntimePluginDefinition Definition {get;}=definition;
    public void Dispose()=>owner?.Dispose();
}
