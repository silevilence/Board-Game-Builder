using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;

Probe.Output=Path.GetFullPath(args.Length>0?args[0]:".scratch/application-loop/windowsdx-contract.json");
AppBuilder.Configure<Probe>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
return Probe.Failed?1:0;

public sealed class Probe : Avalonia.Application
{
    public static string Output="";public static bool Failed;
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var panel=new Grid {ColumnDefinitions=new("*,*")};
            var window=new Window {Title="Rendering contract verification",Width=900,Height=500,Content=panel};
            desktop.MainWindow=window;window.Opened+=async(_,_)=>await Run(window,panel);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    private static async Task Drawn(PlatformViewport viewport,int after=0)
    {
        for(var i=0;i<150;i++)
        {if(viewport.Error!=null)throw new Exception(viewport.Error);if(viewport.Frames>after)return;await Task.Delay(30);}
        throw new Exception("Viewport did not draw");
    }
    private static GraphicsConfiguration Config(RuntimePluginDefinition plugin)=>new(new(1,"graphics","1",
        [new(plugin.Id,plugin.Version,plugin.Api,JsonSerializer.SerializeToElement(new{}))]),[plugin]);
    private static async Task Run(Window window,Grid panel)
    {
        var errors=new List<string>();var screenshots=new List<string>();var input=new List<string>();
        var configuration=Config(WindowsDxBackend.Plugin());
        var files=new Dictionary<string,ReadOnlyMemory<byte>>
        {
            ["image.png"]=File.ReadAllBytes("samples/poker-uno/assets/cards.png"),
            ["font.otf"]=File.ReadAllBytes("samples/cursed-investigators/assets/fonts/noto-sans-cjk-sc/NotoSansCJKsc-Regular.otf")
        };
        var assets=new AssetSet(files,new Dictionary<string,ImageReference>{{"single",new("image.png")},{"rotated",new("image.png",2,2,140,190,true,10,15,220,170)}},new Dictionary<string,string>(),new("font.otf","license"));
        PlatformViewport Create(int column)
        {
            var view=new PlatformViewport(configuration) {MapKey=(key,_,_)=>key==0x41?"a":null};Grid.SetColumn(view,column);
            view.Paint=draw=>
            {
                draw.ConfigureResources(assets);draw.Box(new(5,5,200,200),new(30,90,120));
                draw.Text("中文视口 / 旋转裁剪",10,10,new(255,255,255),20);
                draw.Image("single",assets,new(10,50,180,100));draw.Image("rotated",assets,new(200,50,90,120));
            };
            panel.Children.Add(view);return view;
        }
        PlatformViewport? one=null,two=null;
        try
        {
            foreach(var assembly in new[]{typeof(WindowsDxBackend).Assembly,typeof(PlatformViewport).Assembly})
                Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name is "Kernel" or "Kernel.Abstractions" or "View.Abstractions" or "Runtime"),"Graphics implementation references board code");
            Check(!WindowsDxBackend.Identity.SupportsNativeAot,"WindowsDX falsely claims AOT");
            one=Create(0);two=Create(1);await Drawn(one);await Drawn(two);
            Check(WindowsDxBackend.LiveBackends==1 && WindowsDxBackend.LiveDevices==2,"Window did not share one backend with independent devices");
            one.Input+=(kind,_,_)=>input.Add(kind);one.KeyPressed+=input.Add;
            var point=(nint)((80<<16)|80);
            SendMessage(one.NativeHandle,0x201,0,point);SendMessage(one.NativeHandle,0x202,0,point);
            SendMessage(one.NativeHandle,0x100,0x41,0);SendMessage(one.NativeHandle,0x8,0,0);
            Check(input.Contains("down") && input.Contains("up") && input.Contains("a") && input.Contains("cancel"),"Native mouse/key/focus input missing");
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            for(var cycle=0;cycle<3;cycle++)
            {
                var before=one.Frames;window.Width+=35;window.Height+=20;one.ChangeZoom(1.1f);await Drawn(one,before+2);
                var image=Path.ChangeExtension(Output,$".{cycle}.png");one.SaveImage(image);screenshots.Add(image);
                using(var bitmap=SkiaSharp.SKBitmap.Decode(image))Check(bitmap.Width>100 && bitmap.Height>100,"Capture invalid");
                one.Dispose();panel.Children.Remove(one);await one.Shutdown;
                Check(WindowsDxBackend.LiveDevices==1 && WindowsDxBackend.LiveBackends==1,"Closing one viewport released sibling/backend");
                one=Create(0);await Drawn(one);Check(one.CachedImages==1 && two.CachedImages==1,"Texture caches lost or shared");
            }
            var transient=Create(0);transient.Dispose();panel.Children.Remove(transient);await transient.Shutdown;
            Check(WindowsDxBackend.LiveDevices==2,"Canceled initialization leaked a device");
            one.Dispose();two.Dispose();panel.Children.Clear();await Task.WhenAll(one.Shutdown,two.Shutdown);
            Check(WindowsDxBackend.LiveDevices==0 && WindowsDxBackend.LiveBackends==0,"Graphics resources survived final shutdown");
            var failedPlugin=RenderingPlugin.Backend(new("failure","1","windows-x64",false),()=>new BrokenBackend());
            var failed=new PlatformViewport(Config(failedPlugin));panel.Children.Add(failed);
            for(var i=0;i<100 && failed.Error==null;i++)await Task.Delay(30);
            Check(failed.Error?.Contains("injected device failure")==true,"Device failure not diagnosed");
            failed.Dispose();panel.Children.Remove(failed);await failed.Shutdown;
            one=Create(0);await Drawn(one);one.Dispose();panel.Children.Remove(one);await one.Shutdown;
        }
        catch(Exception error) {errors.Add(error.ToString());}
        finally
        {
            one?.Dispose();two?.Dispose();panel.Children.Clear();
            if(one!=null)await one.Shutdown;if(two!=null)await two.Shutdown;
            for(var i=0;i<100 && PlatformViewport.LiveNativeWindows!=0;i++)await Task.Delay(20);
            window.Close();
            if(PlatformViewport.LiveNativeWindows!=0)errors.Add("Native windows survived top-level close");
            Failed=errors.Count>0;Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            File.WriteAllText(Output,JsonSerializer.Serialize(new {success=!Failed,errors,screenshots,input,devices=WindowsDxBackend.LiveDevices,backends=WindowsDxBackend.LiveBackends,nativeWindows=PlatformViewport.LiveNativeWindows},new JsonSerializerOptions {WriteIndented=true}));
        }
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window,uint message,nuint w,nint l);
    private sealed class BrokenBackend : IRenderBackend
    {
        public RenderBackendDescriptor Descriptor=>new("failure","1","windows-x64",false);
        public IRenderSurface CreateViewport(ViewportTarget target)=>throw new InvalidOperationException("injected device failure");
        public void Dispose() { }
    }
}
