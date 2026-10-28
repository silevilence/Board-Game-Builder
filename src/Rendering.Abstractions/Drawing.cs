using BoardGame.Resources;

namespace BoardGame.Rendering;

public readonly record struct DrawColor(byte R,byte G,byte B,byte A=255)
{
    public DrawColor(int r,int g,int b,int a=255):this((byte)Math.Clamp(r,0,255),(byte)Math.Clamp(g,0,255),(byte)Math.Clamp(b,0,255),(byte)Math.Clamp(a,0,255)) { }
    public static DrawColor White=>new(255,255,255);
    public static DrawColor Black=>new(0,0,0);
}
public readonly record struct DrawPoint(float X,float Y)
{public static DrawPoint operator +(DrawPoint left,DrawPoint right)=>new(left.X+right.X,left.Y+right.Y);}
public readonly record struct DrawRect(int X,int Y,int Width,int Height)
{
    public int Right=>X+Width;
    public int Bottom=>Y+Height;
    public (int X,int Y) Center=>(X+Width/2,Y+Height/2);
}
public readonly record struct DrawTransform(float Scale=1,float X=0,float Y=0);
public sealed record DrawFrame(DrawColor Clear,DrawTransform Transform);
public sealed record ViewportTarget(nint Handle,int Width,int Height,double Scale=1)
{
    public void Validate()
    {
        if(Handle==0 || Width<=0 || Height<=0 || !double.IsFinite(Scale) || Scale<=0)
            throw new ArgumentOutOfRangeException(nameof(ViewportTarget));
    }
}
public sealed record RenderBackendDescriptor(string Id,string Version,string Platform,bool SupportsNativeAot);
public sealed record RenderMetrics(int Images,int Labels,int LabelsCreated);
public sealed record ViewportInput(string Kind,float X=0,float Y=0,string? Key=null,string? Source=null);

/// <summary>Available only during a render callback. Assets contain no device handles.</summary>
public interface IDraw2D
{
    void ConfigureResources(AssetSet resources);
    void Box(DrawRect bounds,DrawColor color);
    void Line(DrawPoint from,DrawPoint to,DrawColor color);
    void Text(string text,int x,int y,DrawColor color,int size=13);
    void Image(string name,AssetSet resources,DrawRect bounds,bool trimTransparent=false,float rotation=0);
}
public interface IDrawingTarget
{
    void Draw(Action<IDraw2D> draw);
}

/// <summary>Provider-side device owner. All calls occur on the graphics thread.
/// Capture draws to a separate target and must restore the normal target on failure.</summary>
public interface IRenderSurface : IDisposable
{
    RenderMetrics Metrics {get;}
    void Render(DrawFrame frame,Action<IDraw2D> draw);
    void Capture(DrawFrame frame,Action<IDraw2D> draw,Stream png);
}
public interface IRenderBackend : IDisposable
{
    RenderBackendDescriptor Descriptor {get;}
    IRenderSurface CreateViewport(ViewportTarget target);
}
