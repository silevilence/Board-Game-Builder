using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
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
    public float Zoom { get; private set; } = 1;
    public float PanX { get; private set; }
    public float PanY { get; private set; }
    public int Frames { get; private set; }
    public static int LiveDevices { get; private set; }
    public string? Error { get; private set; }
    public event Action<string, float, float>? Input;
    public Action<TableRenderer>? Paint { get; set; }
    private bool panning;
    private float lastX, lastY;

    public DesktopViewport()
    {
        procedure = ProcessMessage;
        timer.Tick += (_, _) => Render();
    }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        handle = CreateWindowEx(0, "STATIC", "MonoGame desktop", 0x50000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (handle == 0) throw new System.ComponentModel.Win32Exception();
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
        handle = 0;
        panning = false;
        Input?.Invoke("cancel", 0, 0);
    }
    private void ReleaseGraphics()
    {
        renderer?.Dispose(); renderer = null;
        if (device != null) { device.Dispose(); device = null; LiveDevices--; }
    }
    public void Dispose() { timer.Stop(); ReleaseGraphics(); Paint=null; Input=null; }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        timer.Stop(); ReleaseGraphics(); base.OnDetachedFromVisualTree(e);
    }
    private void Render()
    {
        if (handle == 0 || !GetClientRect(handle, out var rect) || rect.Right < 1 || rect.Bottom < 1) return;
        try
        {
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
            device.Clear(new Color(16, 33, 40));
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
            case 0x201: SetFocus(hwnd); SetCapture(hwnd); Input?.Invoke("down", (x-PanX)/Zoom, (y-PanY)/Zoom); return 0;
            case 0x202: ReleaseCapture(); Input?.Invoke("up", (x-PanX)/Zoom, (y-PanY)/Zoom); return 0;
            case 0x204: SetFocus(hwnd); SetCapture(hwnd); panning = true; lastX=x; lastY=y; return 0;
            case 0x205: panning = false; ReleaseCapture(); return 0;
            case 0x200:
                if (panning) { PanX += x-lastX; PanY += y-lastY; lastX=x; lastY=y; }
                Input?.Invoke("move", (x-PanX)/Zoom, (y-PanY)/Zoom); return 0;
            case 0x20A:
                Zoom = Math.Clamp(Zoom * ((short)((ulong)w >> 16) > 0 ? 1.1f : 1/1.1f), .35f, 3f); return 0;
            case 0x8: case 0x1F: panning=false; Input?.Invoke("cancel", 0, 0); break;
        }
        return CallWindowProc(previous, hwnd, message, w, l);
    }
    private delegate nint WindowProc(nint hwnd, uint message, nuint w, nint l);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint CallWindowProc(nint proc, nint hwnd, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
}
