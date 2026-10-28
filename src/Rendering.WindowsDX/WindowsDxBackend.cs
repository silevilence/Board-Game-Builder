using BoardGame.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BoardGame.Rendering;

public sealed class WindowsDxBackend : IRenderBackend
{
    public static RenderBackendDescriptor Identity {get;}=new("render.windowsdx","1","windows-x64",false);
    public static RuntimePluginDefinition Plugin()=>RenderingPlugin.Backend(Identity,()=>new WindowsDxBackend());
    public RenderBackendDescriptor Descriptor=>Identity;
    public static int LiveDevices {get;private set;}
    public static int LiveBackends {get;private set;}
    private bool disposed;
    public WindowsDxBackend()=>LiveBackends++;
    public IRenderSurface CreateViewport(ViewportTarget target)=>new Surface(target);
    public void Dispose() {if(disposed)return;disposed=true;LiveBackends--;}
    private sealed class Surface : IRenderSurface
    {
        private readonly GraphicsDevice device;
        private readonly WindowsDraw2D drawing;
        private readonly ViewportTarget target;
        private bool disposed;
        public RenderMetrics Metrics=>new(drawing.CachedImages,drawing.CachedLabels,drawing.LabelTexturesCreated);
        public Surface(ViewportTarget target)
        {
            this.target=target;
            device=new GraphicsDevice(GraphicsAdapter.DefaultAdapter,GraphicsProfile.Reach,new PresentationParameters
            {
                DeviceWindowHandle=target.Handle,BackBufferWidth=target.Width,BackBufferHeight=target.Height,
                BackBufferFormat=SurfaceFormat.Color,DepthStencilFormat=DepthFormat.None,
                IsFullScreen=false,PresentationInterval=PresentInterval.Immediate
            });
            try {drawing=new WindowsDraw2D(device);LiveDevices++;}
            catch {device.Dispose();throw;}
        }
        private void Draw(DrawFrame frame,Action<IDraw2D> draw)
        {
            device.Clear(new Color(frame.Clear.R,frame.Clear.G,frame.Clear.B,frame.Clear.A));
            drawing.Begin(frame.Transform.Scale,frame.Transform.X,frame.Transform.Y);
            try {draw(drawing);}finally {drawing.End();}
        }
        public void Render(DrawFrame frame,Action<IDraw2D> draw) {Draw(frame,draw);device.Present();}
        public void Capture(DrawFrame frame,Action<IDraw2D> draw,Stream png)
        {
            using var image=new RenderTarget2D(device,target.Width,target.Height);
            try {device.SetRenderTarget(image);Draw(frame,draw);}
            finally {device.SetRenderTarget(null);}
            image.SaveAsPng(png,target.Width,target.Height);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try {drawing.Dispose();}finally {device.Dispose();LiveDevices--;}
        }
    }
}
