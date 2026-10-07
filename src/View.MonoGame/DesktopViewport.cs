using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace BoardGame.View;

// The graphics device and its resources have exactly the lifetime of the child HWND.
public sealed class DesktopViewport : NativeControlHost, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly WindowProc procedure;
    private nint handle, previous;
    private GraphicsDevice? device;
    private TableRenderer? renderer;
    private int width, height;
    private bool cameraInitialized;
    private bool disposed;
    private double renderScale=1;
    public float Zoom { get; private set; } = 1;
    public float PanX { get; private set; }
    public float PanY { get; private set; }
    public int Frames { get; private set; }
    public static int LiveDevices { get; private set; }
    public static int LiveNativeWindows { get; private set; }
    public string? Error { get; private set; }
    public event Action<string, float, float>? Input;
    public Action<TableRenderer>? Paint { get; set; }
    public Func<float,float,bool>? CanPeek { get; set; }
    public event Action<string>? KeyPressed;
    private bool panning;
    private bool releasingCapture;
    private long lastClick;
    private float clickX,clickY,downX,downY;
    private bool clickCandidate;
    private float lastX, lastY;
    private TopLevel? inputRoot;
    private bool classic,autoFit,sprites;
    private float canvasWidth=920,canvasHeight=640;
    public void Canvas(TableScene scene)
    {
        var hasSprites=scene.Items.Any(i=>i.Kind=="sprite");
        if(classic!=scene.ClassicCards || sprites!=hasSprites) { classic=scene.ClassicCards; sprites=hasSprites; autoFit=classic || sprites; cameraInitialized=false; }
        if((classic || sprites) && scene.Items.Count>0)
        {
            canvasWidth=Math.Max(400,scene.Items.Max(i=>i.Bounds.X+i.Bounds.Width)+20);
            canvasHeight=Math.Max(560,scene.Items.Max(i=>i.Bounds.Y+i.Bounds.Height)+20);
        }
    }
    public void Fit() {autoFit=true;}

    public DesktopViewport()
    {
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
        renderer?.Dispose(); renderer = null;
        if (device != null) { device.Dispose(); device = null; LiveDevices--; }
    }
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        CancelInput();
        inputRoot?.RemoveHandler(PointerWheelChangedEvent,RootWheel); inputRoot=null;
        timer.Stop(); ReleaseGraphics(); Paint=null; Input=null; CanPeek=null; KeyPressed=null;
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        inputRoot=TopLevel.GetTopLevel(this);
        inputRoot?.AddHandler(PointerWheelChangedEvent,RootWheel,RoutingStrategies.Tunnel);
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
        timer.Stop(); ReleaseGraphics(); base.OnDetachedFromVisualTree(e);
    }
    private void Render()
    {
        if (handle == 0 || !GetClientRect(handle, out var rect) || rect.Right < 1 || rect.Bottom < 1) return;
        try
        {
            var scale=TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            if(!cameraInitialized) {renderScale=scale;Zoom=(float)scale;cameraInitialized=true;}
            else if(scale!=renderScale)
            {
                var ratio=(float)(scale/renderScale);Zoom*=ratio;PanX*=ratio;PanY*=ratio;renderScale=scale;
                CancelInput();
            }
            if (device == null || width != rect.Right || height != rect.Bottom)
            {
                ReleaseGraphics(); width = rect.Right; height = rect.Bottom;
                device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach, new PresentationParameters
                {
                    DeviceWindowHandle = handle, BackBufferWidth = width, BackBufferHeight = height,
                    BackBufferFormat = SurfaceFormat.Color, DepthStencilFormat = DepthFormat.None,
                    IsFullScreen = false, PresentationInterval = PresentInterval.Immediate
                });
                LiveDevices++;
                renderer = new TableRenderer(device);
            }
            if(autoFit)
            {
                Zoom=Math.Max(.1f,Math.Min(width/canvasWidth,height/canvasHeight));
                PanX=(width-canvasWidth*Zoom)/2; PanY=0;
            }
            device.Clear(classic?new Color(0,128,43):new Color(16, 33, 40));
            renderer!.Begin(Zoom, PanX, PanY);
            if (Paint != null) Paint(renderer);
            else
            {
                renderer.Box(new Rectangle(40, 50, 600, 340), new Color(26, 67, 70));
                renderer.Text("MONOGAME / TEST TABLE", 65, 70, Color.White);
                for (var i = 0; i < 5; i++) renderer.Box(new Rectangle(70 + i * 100, 150, 80, 120), new Color(226, 197, 140));
                renderer.Text("Wheel: zoom   Right drag: pan   Click: select", 65, 315, Color.White);
            }
            renderer.End(); device.Present(); Frames++; Error = null;
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
                panning=message==0x207 || CanPeek?.Invoke((x-PanX)/Zoom,(y-PanY)/Zoom)!=true; lastX=x; lastY=y;
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
                var key=TableKeys.Map((int)w,(GetKeyState(0x11)&0x8000)!=0,(GetKeyState(0x12)&0x8000)!=0);
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
