using System.Runtime.InteropServices;
using BoardGame.Runtime;
using BoardGame.AppRuntime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BoardGame.Rendering;

public sealed class DesktopGlBackend : IRenderBackend
{
    public static RenderBackendDescriptor Identity {get;}=new("render.desktopgl","1","windows-x64",true);
    public static RuntimePluginDefinition Plugin()=>RenderingPlugin.Backend(Identity,()=>new DesktopGlBackend());
    public static ApprovedApplicationPlugin Create()=>new(_=>Plugin());
    public RenderBackendDescriptor Descriptor=>Identity;
    public static int LiveDevices {get;private set;}
    public static int LiveBackends=>owners;
    private static int owners;
    private static readonly object ownership=new();
    private static int graphicsThread;
    private readonly int thread=Environment.CurrentManagedThreadId;
    private bool disposed;
    public DesktopGlBackend()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("The verified DesktopGL viewport adapter requires Windows");
        lock(ownership)
        {
        if(owners!=0 && graphicsThread!=thread)throw new InvalidOperationException("SDL graphics hosts must share one graphics thread");
        if(owners==0)
        {
            // The platform host owns the Win32 message pump. SDL must not run a second one.
            if(NativeGl.SDL_SetHint("SDL_WINDOWS_ENABLE_MESSAGELOOP","0")==0)throw new InvalidOperationException("Cannot disable the SDL Windows message pump");
            if(NativeGl.SDL_SetHint("SDL_WINDOW_NO_ACTIVATION_WHEN_SHOWN","1")==0)throw new InvalidOperationException("Cannot preserve platform viewport focus");
            if(NativeGl.SDL_InitSubSystem(0x20)!=0)throw new InvalidOperationException(NativeGl.Error);
            try
            {
                foreach(var attribute in new[]{(0,8),(1,8),(2,8),(3,8),(5,1),(6,0),(7,0),(17,2),(18,1)})
                    if(NativeGl.SDL_GL_SetAttribute(attribute.Item1,attribute.Item2)!=0)throw new InvalidOperationException(NativeGl.Error);
            }
            catch{NativeGl.SDL_QuitSubSystem(0x20);throw;}
        }
        graphicsThread=thread;owners++;
        }
    }
    public IRenderSurface CreateViewport(ViewportTarget target)
    {
        Check();target.Validate();return new Surface(target);
    }
    private void Check(){if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("GL requires its graphics thread");ObjectDisposedException.ThrowIf(disposed,this);}
    public void Dispose(){if(disposed)return;Check();disposed=true;lock(ownership){if(--owners==0)NativeGl.SDL_QuitSubSystem(0x20);}}
    private sealed class Surface : IRenderSurface
    {
        private readonly ViewportTarget target;
        private readonly NativeGl.WindowProc procedure;
        private nint window,hwnd,previous;
        private GraphicsDevice? device;
        private GlDraw2D? drawing;
        private bool disposed,counted;
        private readonly int thread=Environment.CurrentManagedThreadId;
        public RenderMetrics Metrics=>new(drawing?.CachedImages ?? 0,drawing?.CachedLabels ?? 0,drawing?.LabelTexturesCreated ?? 0);
        public Surface(ViewportTarget target)
        {
            this.target=target;procedure=Message;
            try
            {
                window=NativeGl.SDL_CreateWindow("render viewport",0,0,target.Width,target.Height,0x2|0x8|0x10|0x20);
                if(window==0)throw new InvalidOperationException(NativeGl.Error);
                var info=new NativeGl.WmInfo();NativeGl.SDL_GetVersion(ref info);
                if(!NativeGl.SDL_GetWindowWMInfo(window,ref info))throw new InvalidOperationException(NativeGl.Error);
                hwnd=info.Window;NativeGl.SetWindowLongPtrW(hwnd,-16,(nint)0x56000000);
                NativeGl.SetParent(hwnd,target.Handle);
                if(NativeGl.GetParent(hwnd)!=target.Handle)throw new InvalidOperationException("GL viewport could not attach to the platform window");
                NativeGl.SetWindowPos(hwnd,0,0,0,target.Width,target.Height,0x34);
                previous=NativeGl.SetWindowLongPtrW(hwnd,-4,Marshal.GetFunctionPointerForDelegate(procedure));
                if(previous==0)throw new System.ComponentModel.Win32Exception();
                NativeGl.SDL_ShowWindow(window);
                device=new(GraphicsAdapter.FromSdlWindow(window),GraphicsProfile.Reach,new PresentationParameters
                {DeviceWindowHandle=window,BackBufferWidth=target.Width,BackBufferHeight=target.Height,
                    DepthStencilFormat=DepthFormat.None,PresentationInterval=PresentInterval.Immediate});
                device.MakeCurrent();drawing=new(device);LiveDevices++;counted=true;
            }
            catch{Dispose();throw;}
        }
        private nint Message(nint handle,uint message,nuint w,nint l)
        {
            // The platform viewport retains all input/capture/coordinate semantics.
            // The child covers its entire client rectangle, so local coordinates coincide.
            if(message==7){NativeGl.SetFocus(target.Handle);return 0;}
            if(message is 0x200 or 0x201 or 0x202 or 0x203 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20A or 0x100 or 0x101 or 0x104 or 0x105)
                return NativeGl.SendMessageW(target.Handle,message,w,l);
            return NativeGl.CallWindowProcW(previous,handle,message,w,l);
        }
        private void Draw(DrawFrame frame,Action<IDraw2D> draw)
        {
            Check();device!.MakeCurrent();
            device.Clear(new Color(frame.Clear.R,frame.Clear.G,frame.Clear.B,frame.Clear.A));
            drawing!.Begin(frame.Transform.Scale,frame.Transform.X,frame.Transform.Y);
            try{draw(drawing);}finally{drawing.End();}
        }
        public void Render(DrawFrame frame,Action<IDraw2D> draw){Draw(frame,draw);device!.Present();NativeGl.DrainEvents();}
        public void Capture(DrawFrame frame,Action<IDraw2D> draw,Stream png)
        {
            Check();device!.MakeCurrent();
            using var image=new RenderTarget2D(device,target.Width,target.Height);
            try{device.SetRenderTarget(image);Draw(frame,draw);}
            finally{device.SetRenderTarget(null);}
            image.SaveAsPng(png,target.Width,target.Height);
        }
        public void Dispose()
        {
            if(disposed)return;Check();disposed=true;
            try{if(device!=null){device.MakeCurrent();try{drawing?.Dispose();}finally{device.Dispose();}}}
            finally
            {
                if(hwnd!=0 && previous!=0)NativeGl.SetWindowLongPtrW(hwnd,-4,previous);
                if(window!=0)NativeGl.SDL_DestroyWindow(window);
                if(counted){LiveDevices--;counted=false;}
                device=null;drawing=null;hwnd=0;window=0;
            }
        }
        private void Check(){if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("GL requires its graphics thread");ObjectDisposedException.ThrowIf(disposed,this);}
    }
}

internal static partial class NativeGl
{
    internal delegate nint WindowProc(nint hwnd,uint message,nuint w,nint l);
    internal static string Error=>Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL error";
    internal static unsafe void DrainEvents(){byte* data=stackalloc byte[64*64];while(SDL_PeepEvents((nint)data,64,2,0,0xffff)>0){}}
    [StructLayout(LayoutKind.Explicit,Size=128)] internal struct WmInfo
    {[FieldOffset(0)]public byte Major;[FieldOffset(1)]public byte Minor;[FieldOffset(2)]public byte Patch;[FieldOffset(4)]public int Subsystem;[FieldOffset(8)]public nint Window;}
    [LibraryImport("SDL2.dll",StringMarshalling=StringMarshalling.Utf8)]internal static partial int SDL_SetHint(string name,string value);
    [LibraryImport("SDL2.dll")]internal static partial int SDL_InitSubSystem(uint flags);
    [LibraryImport("SDL2.dll")]internal static partial void SDL_QuitSubSystem(uint flags);
    [LibraryImport("SDL2.dll")]private static partial nint SDL_GetError();
    [LibraryImport("SDL2.dll")]internal static partial int SDL_GL_SetAttribute(int attribute,int value);
    [LibraryImport("SDL2.dll",StringMarshalling=StringMarshalling.Utf8)]internal static partial nint SDL_CreateWindow(string title,int x,int y,int w,int h,uint flags);
    [LibraryImport("SDL2.dll")]internal static partial void SDL_DestroyWindow(nint window);
    [LibraryImport("SDL2.dll")]internal static partial void SDL_ShowWindow(nint window);
    [LibraryImport("SDL2.dll")]internal static partial void SDL_GetVersion(ref WmInfo version);
    [LibraryImport("SDL2.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static partial bool SDL_GetWindowWMInfo(nint window,ref WmInfo info);
    [LibraryImport("SDL2.dll")]private static partial int SDL_PeepEvents(nint data,int count,int action,uint min,uint max);
    [LibraryImport("user32.dll")]internal static partial nint SetParent(nint child,nint parent);
    [LibraryImport("user32.dll")]internal static partial nint GetParent(nint child);
    [LibraryImport("user32.dll")]internal static partial nint SetWindowLongPtrW(nint hwnd,int index,nint value);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static partial bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    [LibraryImport("user32.dll")]internal static partial nint SendMessageW(nint hwnd,uint msg,nuint w,nint l);
    [LibraryImport("user32.dll")]internal static partial nint CallWindowProcW(nint previous,nint hwnd,uint msg,nuint w,nint l);
    [LibraryImport("user32.dll")]internal static partial nint SetFocus(nint hwnd);
}
