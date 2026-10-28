using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using BoardGame.Runtime;
namespace BoardGame.Rendering;

// The graphics device and its resources have exactly the lifetime of the child HWND.
public class PlatformViewport : NativeControlHost, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly WindowProc procedure;
    private nint handle, previous;
    public nint NativeHandle=>handle;
    private RenderViewport? surface;
    private RuntimeHost? renderScope;
    private readonly GraphicsConfiguration configuration;
    private readonly ViewportComposition? presentation;
    private readonly DrawingTarget drawing=new();
    private GraphicsWindowHost? graphicsHost;
    private int generation;
    private TimeSpan lastFrame;
    private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    public Task Shutdown {get;private set;}=Task.CompletedTask;
    public Func<int,bool,bool,string?>? MapKey {get;set;}
    public DrawColor Background {get;set;}=new(16,33,40);
    private int width, height;
    private bool cameraInitialized;
    private bool disposed;
    private double renderScale=1;
    public float Zoom { get; private set; } = 1;
    public float PanX { get; private set; }
    public float PanY { get; private set; }
    public int Frames { get; private set; }
    public int CachedImages=>surface?.Metrics.Images ?? 0;
    public RenderMetrics Metrics=>surface?.Metrics ?? new(0,0,0);
    public static int LiveNativeWindows { get; private set; }
    public string? Error { get; private set; }
    public event Action<string, float, float>? Input;
    public Action<IDraw2D>? Paint { get; set; }
    public Func<float,float,bool>? UsesSecondaryPointer { get; set; }
    public event Action<string>? KeyPressed;
    private bool panning;
    private bool releasingCapture;
    private long lastClick;
    private float clickX,clickY,downX,downY;
    private bool clickCandidate;
    private float lastX, lastY;
    private TopLevel? inputRoot;
    private bool autoFit;
    private float canvasWidth=920,canvasHeight=640;
    public void Canvas(float width,float height,bool? fit=null)
    {
        canvasWidth=width;canvasHeight=height;if(fit.HasValue) {autoFit=fit.Value;cameraInitialized=false;}
    }
    public void Fit() {autoFit=true;}
    public void SaveImage(string path)
    {
        if(surface==null || Frames==0) throw new InvalidOperationException("Viewport has not rendered yet");
        using var stream=File.Create(path);surface.Capture(Frame(),draw=>drawing.Present(draw,()=>renderScope!.Render(TimeSpan.Zero)),stream);
    }

    public PlatformViewport(GraphicsConfiguration configuration,ViewportComposition? presentation=null)
    {
        this.configuration=configuration with {Startup=configuration.Startup with {
            Plugins=configuration.Startup.Plugins.Select(p=>p with {Configuration=p.Configuration.Clone()}).ToList()},Catalog=configuration.Catalog.ToArray()};
        this.presentation=presentation==null?null:presentation with {
            Plugins=presentation.Plugins.Select(p=>p with {Configuration=p.Configuration.Clone()}).ToArray(),Catalog=presentation.Catalog.ToArray()};
        procedure = ProcessMessage;
        Focusable=true;
        timer.Tick += (_, _) => Render();
    }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        // SS_NOTIFY makes STATIC return HTCLIENT instead of HTTRANSPARENT for real mouse input.
        handle = CreateWindowEx(0, "STATIC", "MonoGame desktop", 0x50000100, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (handle == 0) throw new System.ComponentModel.Win32Exception();
        LiveNativeWindows++;
        previous = SetWindowLongPtr(handle, -4, Marshal.GetFunctionPointerForDelegate(procedure));
        timer.Start();
        return new PlatformHandle(handle, "HWND");
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        timer.Stop();
        ReleaseGraphics();
        SetWindowLongPtr(control.Handle, -4, previous);
        DestroyWindow(control.Handle);
        LiveNativeWindows--;
        handle = 0;
        panning = false;
        Input?.Invoke("cancel", 0, 0);
    }
    private void ReleaseGraphics()
    {
        surface?.Dispose();surface=null;
    }
    public virtual void Dispose()
    {
        if(disposed) return;disposed=true;
        CancelInput();
        inputRoot?.RemoveHandler(PointerWheelChangedEvent,RootWheel); inputRoot=null;
        timer.Stop(); ReleaseGraphics();ReleaseAttachment(); Paint=null; Input=null; UsesSecondaryPointer=null; KeyPressed=null;
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        inputRoot=TopLevel.GetTopLevel(this);
        inputRoot?.AddHandler(PointerWheelChangedEvent,RootWheel,RoutingStrategies.Tunnel);
        AttachGraphics();
    }
    private void RootWheel(object? sender,PointerWheelEventArgs e)
    {
        var point=e.GetPosition(this);
        if(point.X>=0 && point.Y>=0 && point.X<Bounds.Width && point.Y<Bounds.Height) { ChangeZoom(e.Delta.Y>0?1.1f:1/1.1f); e.Handled=true; }
    }
    public void ChangeZoom(float factor) {autoFit=false; Zoom=Math.Clamp(Zoom*factor,.35f,3f);}
    public void ResetClickSequence() { lastClick=0; clickCandidate=false; }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        CancelInput();
        inputRoot?.RemoveHandler(PointerWheelChangedEvent,RootWheel); inputRoot=null;
        timer.Stop(); ReleaseGraphics();ReleaseAttachment(); base.OnDetachedFromVisualTree(e);
    }
    private async void AttachGraphics()
    {
        var current=++generation;
        try
        {
            if(disposed)return;
            graphicsHost=GraphicsWindowHost.Acquire(TopLevel.GetTopLevel(this)!,configuration);
            var instance=await graphicsHost.Instance;
            if(disposed || current!=generation)return;
            var target=new RuntimePluginDefinition("platform.viewport","1",1,["render.target"],[RenderingPlugin.Capability],scope=>scope.Provide("render.target",drawing))
                {Execution=new(HostMode.Graphical,RuntimeScope.Viewport)};
            var callback=new RuntimePluginDefinition("render.callback","1",1,[],["render.target"],scope=>
            {var output=scope.Service<IDrawingTarget>("render.target");scope.Render("draw",_=>output.Draw(draw=>Paint?.Invoke(draw)));})
                {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
            BoardGame.Kernel.RuntimePluginSelection Select(RuntimePluginDefinition p)=>new(p.Id,p.Version,p.Api,System.Text.Json.JsonSerializer.SerializeToElement(new{}));
            var content=presentation ?? new ViewportComposition([Select(callback)],[callback]);
            var startup=configuration.Startup with {Plugins=[..configuration.Startup.Plugins,Select(target),..content.Plugins]};
            var next=await RuntimeHost.StartScopedAsync(startup,[..configuration.Catalog,target,..content.Catalog],[],HostMode.Graphical,
                RuntimeScope.Viewport,instance,dispatch:GraphicsWindowHost.Dispatch);
            if(disposed || current!=generation) {await next.DisposeAsync();return;}
            renderScope=next;lastFrame=clock.Elapsed;timer.Start();
        }
        catch(Exception error) {if(!disposed && current==generation) {Error=error.Message;timer.Stop();Input?.Invoke("error",0,0);}}
    }
    private void ReleaseAttachment()
    {
        generation++;
        var scope=renderScope;renderScope=null;var owner=graphicsHost;graphicsHost=null;
        Shutdown=Task.WhenAll(Shutdown,CloseAsync(scope,owner));
    }
    private async Task CloseAsync(RuntimeHost? scope,GraphicsWindowHost? owner)
    {
        // Release is called on UI before awaiting runtime cleanup, so its reference count is serialized.
        var root=owner?.Release() ?? Task.CompletedTask;
        try {if(scope!=null)await scope.DisposeAsync();await root;}
        catch(Exception error) {Error=error.Message;}
    }
    private DrawFrame Frame()=>new(Background,new(Zoom,PanX,PanY));
    private void Render()
    {
        if (disposed || renderScope==null || handle == 0 || !GetClientRect(handle, out var rect) || rect.Right < 1 || rect.Bottom < 1) return;
        try
        {
            var scale=TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            if(!cameraInitialized) {renderScale=scale;Zoom=(float)scale;cameraInitialized=true;}
            else if(scale!=renderScale)
            {
                var ratio=(float)(scale/renderScale);Zoom*=ratio;PanX*=ratio;PanY*=ratio;renderScale=scale;
                CancelInput();
            }
            if (surface == null || width != rect.Right || height != rect.Bottom)
            {
                ReleaseGraphics(); width = rect.Right; height = rect.Bottom;
                surface=renderScope.Service<RenderBackend>(RenderingPlugin.Capability).CreateViewport(new(handle,width,height,scale));
            }
            if(autoFit)
            {
                Zoom=Math.Max(.1f,Math.Min(width/canvasWidth,height/canvasHeight));
                PanX=(width-canvasWidth*Zoom)/2; PanY=0;
            }
            var now=clock.Elapsed;surface!.Render(Frame(),context=>drawing.Present(context,()=>renderScope.Render(now-lastFrame)));lastFrame=now;Frames++;Error=null;
        }
        catch (Exception e) { Error = e.Message; ReleaseGraphics(); timer.Stop(); Input?.Invoke("error", 0, 0); }
    }
    private nint ProcessMessage(nint hwnd, uint message, nuint w, nint l)
    {
        float x = (short)((long)l & 0xffff), y = (short)(((long)l >> 16) & 0xffff);
        switch (message)
        {
            case 0x201: case 0x203:
                Focus(); SetFocus(hwnd); SetCapture(hwnd);
                downX=x; downY=y; clickCandidate=true;
                var isDouble=lastClick>0 && Environment.TickCount64-lastClick<=GetDoubleClickTime()
                    && Math.Abs(x-clickX)<=GetSystemMetrics(36) && Math.Abs(y-clickY)<=GetSystemMetrics(37);
                if(isDouble) { lastClick=0; clickCandidate=false; }
                Input?.Invoke(isDouble?"double-click":"down",(x-PanX)/Zoom,(y-PanY)/Zoom); return 0;
            case 0x202:
                if(clickCandidate && Math.Abs(x-downX)+Math.Abs(y-downY)<=8*Zoom) {lastClick=Environment.TickCount64;clickX=x;clickY=y;}
                else lastClick=0;
                clickCandidate=false; Input?.Invoke("up",(x-PanX)/Zoom,(y-PanY)/Zoom); ReleasePointer(); return 0;
            case 0x204: case 0x207:
                Focus(); SetFocus(hwnd); SetCapture(hwnd); lastClick=0; clickCandidate=false;
                panning=message==0x207 || UsesSecondaryPointer?.Invoke((x-PanX)/Zoom,(y-PanY)/Zoom)!=true; lastX=x; lastY=y;
                Input?.Invoke(panning?"cancel":"right-down",(x-PanX)/Zoom,(y-PanY)/Zoom); return 0;
            case 0x205: case 0x208:
                panning=false; Input?.Invoke("right-up",(x-PanX)/Zoom,(y-PanY)/Zoom); ReleasePointer(); return 0;
            case 0x200:
                if (panning) { autoFit=false; PanX += x-lastX; PanY += y-lastY; lastX=x; lastY=y; }
                if(Math.Abs(x-downX)+Math.Abs(y-downY)>8*Zoom) clickCandidate=false;
                Input?.Invoke("move", (x-PanX)/Zoom, (y-PanY)/Zoom); return 0;
            case 0x20A:
                ChangeZoom((short)((ulong)w >> 16) > 0 ? 1.1f : 1/1.1f); return 0;
            case 0x100: case 0x104:
                var key=MapKey?.Invoke((int)w,(GetKeyState(0x11)&0x8000)!=0,(GetKeyState(0x12)&0x8000)!=0);
                if(key!=null) { if(((long)l & (1L<<30))==0) KeyPressed?.Invoke(key); return 0; }
                break;
            case 0x215:
                if(releasingCapture) break;
                goto case 0x1F;
            case 0x8: case 0x1F: CancelInput();break;
        }
        return CallWindowProc(previous, hwnd, message, w, l);
    }
    private delegate nint WindowProc(nint hwnd, uint message, nuint w, nint l);
    public void CancelInput()
    {
        panning=false;ResetClickSequence();
        if(handle!=0 && GetCapture()==handle) ReleasePointer();
        Input?.Invoke("cancel",0,0);
    }
    private void ReleasePointer() { releasingCapture=true; try {ReleaseCapture();} finally {releasingCapture=false;} }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint CallWindowProc(nint proc, nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
}
