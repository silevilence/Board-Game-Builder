using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

var evidence=Path.GetFullPath(args.FirstOrDefault() ?? "gl-embedded-evidence");
Directory.CreateDirectory(evidence);
var log=new List<string> {"dynamicCodeSupported="+RuntimeFeature.IsDynamicCodeSupported};
try
{
    Native.SetProcessDPIAware();
    if(Native.SDL_Init(0x20)!=0)throw new InvalidOperationException(Native.Error);
    // Match the pinned MonoGame DesktopGL context attributes; SDL owns each window's pixel format.
    foreach(var attribute in new[]{(0,8),(1,8),(2,8),(3,8),(5,1),(6,0),(7,0),(17,2),(18,1)})
        if(Native.SDL_GL_SetAttribute(attribute.Item1,attribute.Item2)!=0)throw new InvalidOperationException(Native.Error);
    var invalidWindow=Native.SDL_CreateWindow("no GL",0,0,10,10,0x8);
    if(invalidWindow==0)throw new InvalidOperationException(Native.Error);
    try
    {
        try
        {
            using var invalid=new GraphicsDevice(GraphicsAdapter.FromSdlWindow(invalidWindow),GraphicsProfile.Reach,new PresentationParameters
            {DeviceWindowHandle=invalidWindow,BackBufferWidth=10,BackBufferHeight=10});
            throw new InvalidDataException("Non-GL window unexpectedly accepted");
        }
        catch(InvalidOperationException error) when(error.Message.StartsWith("SDL GL context creation failed:",StringComparison.Ordinal))
        {log.Add("PASS non-GL window rejected without a current context");}
        if(Native.SDL_GL_GetCurrentContext()!=0)throw new InvalidOperationException("Failed context creation left a current context");
    }
    finally {Native.SDL_DestroyWindow(invalidWindow);}
    for(var pass=0;pass<10;pass++)
    {
        var parent=Native.CreateWindowExW(0,"STATIC","MonoGame native embedded multi-viewport probe",0x10CF0000,40,40,900,520,0,0,0,0);
        if(parent==0)throw new System.ComponentModel.Win32Exception();
        try
        {
            using var first=new Surface(parent,10,20,400,350,new Color(70,24,28));
            using var second=new Surface(parent,430,20,400,350,new Color(20,44,85));
            first.VerifyMouseRouting();second.VerifyMouseRouting();
            first.VerifyThreadGuard();
            for(var frame=0;frame<24;frame++)
            {
                if(frame==8) {first.Resize(360,390);second.Resize(440,390);}
                first.Draw(evidence,$"pass-{pass}-a",frame==12);
                second.Draw(evidence,$"pass-{pass}-b",frame==12);
                Native.Pump();
                Thread.Sleep(10);
            }
            // Dispose one while another survives: no global window/context ownership is allowed.
            first.Dispose();
            second.Draw(evidence,$"pass-{pass}-survivor",true);
            log.Add($"PASS pass={pass}: two embedded SDL windows; routed HWND mouse events; thread guard; 48 alternating frames; resize; first disposed; survivor rendered");
        }
        finally {Native.DestroyWindow(parent);}
        Native.Pump();
        if(Surface.Live!=0)throw new InvalidOperationException("Surface ownership leaked");
        if(Native.SDL_GL_GetCurrentContext()!=0)throw new InvalidOperationException("Disposed GL context remains current");
        log.Add($"native resources after pass={pass}: GDI={Native.GetGuiResources(Native.GetCurrentProcess(),0)}, USER={Native.GetGuiResources(Native.GetCurrentProcess(),1)}");
    }
    log.Add("PASS owned surfaces returned to zero after every cycle");
    File.WriteAllLines(Path.Combine(evidence,"result.txt"),log);
    return 0;
}
catch(Exception error)
{
    log.Add(error.ToString());File.WriteAllLines(Path.Combine(evidence,"result.txt"),log);Console.Error.WriteLine(error);return 1;
}
finally {Native.SDL_Quit();}

sealed class Surface : IDisposable
{
    private nint window,hwnd;
    private GraphicsDevice? device;
    private SpriteBatch? batch;
    private Texture2D? picture,label;
    private readonly Color clear;
    private bool disposed,counted;
    private int width,height;
    public static int Live {get;private set;}
    public Surface(nint parent,int x,int y,int width,int height,Color clear)
    {
        this.clear=clear;this.width=width;this.height=height;
        try
        {
            window=Native.SDL_CreateWindow("embedded",0,0,width,height,0x2|0x8|0x10|0x20);
            if(window==0)throw new InvalidOperationException(Native.Error);
            var info=new Native.WmInfo();Native.SDL_GetVersion(ref info);
            if(!Native.SDL_GetWindowWMInfo(window,ref info))throw new InvalidOperationException(Native.Error);
            hwnd=info.Window;
            Native.SetWindowLongPtrW(hwnd,-16,(nint)0x56000000); // child, visible, clip siblings/children
            Native.SetParent(hwnd,parent);
            if(Native.GetParent(hwnd)!=parent)throw new InvalidOperationException("SDL child was not embedded");
            Native.SetWindowPos(hwnd,0,x,y,width,height,0x34);
            Native.SDL_ShowWindow(window);
            device=new GraphicsDevice(GraphicsAdapter.FromSdlWindow(window),GraphicsProfile.Reach,new PresentationParameters
            {DeviceWindowHandle=window,BackBufferWidth=width,BackBufferHeight=height,DepthStencilFormat=DepthFormat.None,PresentationInterval=PresentInterval.Immediate});
            device.MakeCurrent();batch=new(device);
            using(var input=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"marker.png")))picture=Texture2D.FromStream(device,input);
            using var typeface=SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory,"font.otf"));
            using var font=new SKFont(typeface,22);
            const string text="原生嵌入：中文与图片";
            if(!font.ContainsGlyphs(text))throw new InvalidDataException("Missing CJK glyphs");
            using var bitmap=new SKBitmap(360,44,SKColorType.Rgba8888,SKAlphaType.Premul);
            using(var canvas=new SKCanvas(bitmap))
            {canvas.Clear(SKColors.Transparent);using var paint=new SKPaint {Color=SKColors.White,IsAntialias=true};canvas.DrawText(text,4,30,SKTextAlign.Left,font,paint);}
            using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(png.ToArray());
            label=Texture2D.FromStream(device,stream);
            Live++;counted=true;
        }
        catch {Dispose();throw;}
    }
    public void Resize(int nextWidth,int nextHeight)
    {
        device!.MakeCurrent();Native.SDL_SetWindowSize(window,nextWidth,nextHeight);Native.SetWindowPos(hwnd,0,0,0,nextWidth,nextHeight,0x16);
        for(var attempt=0;attempt<50;attempt++)
        {
            Native.Pump();Native.SDL_GL_GetDrawableSize(window,out width,out height);
            if(width==nextWidth && height==nextHeight)break;
            Thread.Sleep(10);
        }
        if(width!=nextWidth || height!=nextHeight){Native.GetClientRect(hwnd,out var rect);throw new InvalidOperationException($"SDL drawable did not resize: {width}x{height}, native {rect.Right}x{rect.Bottom}, requested {nextWidth}x{nextHeight}");}
        device.PresentationParameters.BackBufferWidth=width;device.PresentationParameters.BackBufferHeight=height;
        device.Viewport=new Viewport(0,0,width,height);
    }
    public void VerifyMouseRouting()
    {
        var id=Native.SDL_GetWindowID(window);
        Native.MouseDown.Remove(id);
        Native.SendMessageW(hwnd,0x201,1,(nint)((50<<16)|40));
        Native.SendMessageW(hwnd,0x202,0,(nint)((50<<16)|40));
        Native.Pump();
        if(!Native.MouseDown.Contains(id))throw new InvalidOperationException("Mouse event did not reach its SDL child window");
    }
    public void VerifyThreadGuard()
    {
        var rejected=Task.Run(()=>{try{device!.MakeCurrent();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();
        if(!rejected)throw new InvalidDataException("Graphics thread guard was bypassed");
    }
    public void Draw(string evidence,string name,bool capture)
    {
        device!.MakeCurrent();device.Clear(clear);
        batch!.Begin();batch.Draw(label!,new Vector2(5,8),Color.White);batch.Draw(picture!,new Rectangle(30,80,120,120),Color.White);batch.End();
        var pixel=new Color[1];device.GetBackBufferData(new Rectangle(width-2,height-2,1,1),pixel,0,1);
        var wrong=pixel[0].R!=clear.R || pixel[0].G!=clear.G || pixel[0].B!=clear.B;
        if(capture || wrong)
        {
            var pixels=new Color[width*height];device.GetBackBufferData(pixels);
            using var image=new Texture2D(device,width,height);image.SetData(pixels);
            using var output=File.Create(Path.Combine(evidence,name+".png"));image.SaveAsPng(output,width,height);
        }
        if(wrong)throw new InvalidOperationException("Wrong context/backbuffer pixel: "+pixel[0]);
        device.Present();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(device!=null){device.MakeCurrent();label?.Dispose();picture?.Dispose();batch?.Dispose();device.Dispose();device=null;}
        if(window!=0){Native.SDL_DestroyWindow(window);window=0;if(Native.IsWindow(hwnd))throw new InvalidOperationException("SDL child HWND was not destroyed");}
        if(counted){Live--;counted=false;}
    }
}

static partial class Native
{
    public static string Error=>Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL error";
    public static readonly HashSet<uint> MouseDown=[];
    public static unsafe void Pump(){byte* data=stackalloc byte[64];while(SDL_PollEvent((nint)data)!=0){if(*(uint*)data==0x401)MouseDown.Add(*(uint*)(data+8));}}
    [StructLayout(LayoutKind.Explicit,Size=128)] public struct WmInfo
    {[FieldOffset(0)]public byte Major;[FieldOffset(1)]public byte Minor;[FieldOffset(2)]public byte Patch;[FieldOffset(4)]public int Subsystem;[FieldOffset(8)]public nint Window;}
    [StructLayout(LayoutKind.Sequential)]public struct Rect {public int Left,Top,Right,Bottom;}
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool GetClientRect(nint hwnd,out Rect rect);
    [LibraryImport("SDL2.dll")] public static partial int SDL_Init(uint flags);
    [LibraryImport("SDL2.dll")] public static partial void SDL_Quit();
    [LibraryImport("SDL2.dll")] private static partial nint SDL_GetError();
    [LibraryImport("SDL2.dll")] public static partial int SDL_GL_SetAttribute(int attribute,int value);
    [LibraryImport("SDL2.dll",StringMarshalling=StringMarshalling.Utf8)]public static partial nint SDL_CreateWindow(string title,int x,int y,int w,int h,uint flags);
    [LibraryImport("SDL2.dll")]public static partial void SDL_DestroyWindow(nint window);
    [LibraryImport("SDL2.dll")]public static partial void SDL_ShowWindow(nint window);
    [LibraryImport("SDL2.dll")]public static partial void SDL_SetWindowSize(nint window,int w,int h);
    [LibraryImport("SDL2.dll")]public static partial void SDL_GL_GetDrawableSize(nint window,out int w,out int h);
    [LibraryImport("SDL2.dll")]public static partial nint SDL_GL_GetCurrentContext();
    [LibraryImport("SDL2.dll")]public static partial uint SDL_GetWindowID(nint window);
    [LibraryImport("SDL2.dll")]public static partial void SDL_GetVersion(ref WmInfo version);
    [LibraryImport("SDL2.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool SDL_GetWindowWMInfo(nint window,ref WmInfo info);
    [LibraryImport("SDL2.dll")]private static partial int SDL_PollEvent(nint data);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool SetProcessDPIAware();
    [LibraryImport("user32.dll",StringMarshalling=StringMarshalling.Utf16)]public static partial nint CreateWindowExW(uint ex,string cls,string title,uint style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [LibraryImport("user32.dll")]public static partial nint SetParent(nint child,nint parent);
    [LibraryImport("user32.dll")]public static partial nint GetParent(nint child);
    [LibraryImport("user32.dll")]public static partial nint SetWindowLongPtrW(nint hwnd,int index,nint value);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")]public static partial nint SendMessageW(nint hwnd,uint msg,nuint w,nint l);
    [LibraryImport("user32.dll")]public static partial uint GetGuiResources(nint process,uint flags);
    [LibraryImport("kernel32.dll")]public static partial nint GetCurrentProcess();
}
