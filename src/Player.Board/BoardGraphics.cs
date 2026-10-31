using BoardGame.Kernel;
using BoardGame.Rendering;

namespace BoardGame.Player;

internal sealed class BoardGraphics:IDisposable
{
    private readonly GraphicsBackendLease? lease;
    public GraphicsConfiguration? Configuration {get;}
    public BoardGraphics(RuntimeStartup startup)
    {
        if(!startup.Plugins.Any(p=>GraphicsBackendContract.Descriptions.Any(d=>d.Id==p.Id)))return;
        lease=new GraphicsBackendPackages(WindowsDxBackend.Plugin,Path.Combine(AppContext.BaseDirectory,"backends","desktopgl")).Open(startup);
        Configuration=new(lease.Startup,[lease.Definition]);
    }
    public void Dispose()=>lease?.Dispose();
}
