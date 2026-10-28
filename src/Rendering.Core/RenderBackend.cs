using BoardGame.Resources;

namespace BoardGame.Rendering;

/// <summary>Thread and lifetime checks shared by approved backend implementations.</summary>
public sealed class RenderBackend : IDisposable
{
    private readonly IRenderBackend backend;
    private readonly int thread=Environment.CurrentManagedThreadId;
    private readonly List<RenderViewport> viewports=[];
    private bool disposed;
    public RenderBackendDescriptor Descriptor {get;}
    internal RenderBackend(IRenderBackend backend) {this.backend=backend;Descriptor=backend.Descriptor;}
    internal void Check()
    {
        if(Environment.CurrentManagedThreadId!=thread) throw new InvalidOperationException("Render device requires its graphics thread");
        ObjectDisposedException.ThrowIf(disposed,this);
    }
    public RenderViewport CreateViewport(ViewportTarget target)
    {
        Check();target.Validate();
        var result=new RenderViewport(this,backend.CreateViewport(target),target);
        viewports.Add(result);return result;
    }
    internal void Removed(RenderViewport viewport)=>viewports.Remove(viewport);
    public void Dispose()
    {
        if(disposed)return;Check();
        if(viewports.Any(v=>v.Rendering)) throw new InvalidOperationException("Cannot release backend during a frame");
        List<Exception> errors=[];
        foreach(var viewport in viewports.ToArray().Reverse())
            try {viewport.Dispose();}catch(Exception error) {errors.Add(error);}
        disposed=true;
        try {backend.Dispose();}catch(Exception error) {errors.Add(error);}
        if(errors.Count>0) throw new AggregateException("Render backend cleanup failed",errors);
    }
}

public sealed class RenderViewport : IDisposable
{
    private readonly RenderBackend owner;
    private readonly IRenderSurface surface;
    private bool disposed;
    internal bool Rendering {get;private set;}
    public ViewportTarget Target {get;}
    internal RenderViewport(RenderBackend owner,IRenderSurface surface,ViewportTarget target)
    {this.owner=owner;this.surface=surface;Target=target;}
    private void Check() {owner.Check();ObjectDisposedException.ThrowIf(disposed,this);}
    public RenderMetrics Metrics {get {Check();return surface.Metrics;}}
    public void Render(DrawFrame frame,Action<IDraw2D> draw)=>Draw(frame,draw,null);
    public void Capture(DrawFrame frame,Action<IDraw2D> draw,Stream png)
    {ArgumentNullException.ThrowIfNull(png);if(!png.CanWrite)throw new ArgumentException("PNG stream is not writable",nameof(png));Draw(frame,draw,png);}
    private void Draw(DrawFrame frame,Action<IDraw2D> draw,Stream? png)
    {
        Check();ArgumentNullException.ThrowIfNull(draw);
        if(Rendering)throw new InvalidOperationException("Render reentry");
        if(!float.IsFinite(frame.Transform.Scale) || frame.Transform.Scale<=0 || !float.IsFinite(frame.Transform.X) || !float.IsFinite(frame.Transform.Y))
            throw new ArgumentOutOfRangeException(nameof(frame));
        Rendering=true;
        try
        {
            void Invoke(IDraw2D context)
            {
                var lease=new DrawingLease(this,context);
                try {draw(lease);}finally {lease.Active=false;}
            }
            if(png==null)surface.Render(frame,Invoke);else surface.Capture(frame,Invoke,png);
        }
        finally {Rendering=false;}
    }
    public void Dispose()
    {
        if(disposed)return;Check();
        if(Rendering)throw new InvalidOperationException("Cannot release viewport during a frame");
        disposed=true;owner.Removed(this);surface.Dispose();
    }
    private sealed class DrawingLease(RenderViewport viewport,IDraw2D draw) : IDraw2D
    {
        internal bool Active=true;
        private void Check() {viewport.Check();if(!Active)throw new InvalidOperationException("Drawing context expired");}
        public void ConfigureResources(AssetSet resources) {Check();draw.ConfigureResources(resources);}
        public void Box(DrawRect bounds,DrawColor color) {Check();draw.Box(bounds,color);}
        public void Line(DrawPoint from,DrawPoint to,DrawColor color) {Check();draw.Line(from,to,color);}
        public void Text(string text,int x,int y,DrawColor color,int size=13) {Check();draw.Text(text,x,y,color,size);}
        public void Image(string name,AssetSet resources,DrawRect bounds,bool trimTransparent=false,float rotation=0)
        {Check();draw.Image(name,resources,bounds,trimTransparent,rotation);}
    }
}
