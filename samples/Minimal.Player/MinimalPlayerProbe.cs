using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using Sample.Motion;

// Verification belongs to the sample installation, not to the generic host or rendering contracts.
public sealed class MinimalPlayerProbe : Avalonia.Application
{
    private static string source="",output="";
    private static GraphicsConfiguration graphics=null!;
    public static int Run(string package,string report,GraphicsConfiguration configuration)
    {
        source=package;output=Path.GetFullPath(report);graphics=configuration;
        return AppBuilder.Configure<MinimalPlayerProbe>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var launcher=new Window{Width=300,Height=100};desktop.MainWindow=launcher;
            launcher.Opened+=async(_,_)=>await Verify(desktop,launcher);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static async Task Until(Func<bool> condition,string message)
    {for(var i=0;i<600;i++){if(condition())return;await Task.Delay(30);}throw new InvalidOperationException(message);}
    private static async Task Verify(IClassicDesktopStyleApplicationLifetime desktop,Window launcher)
    {
        var errors=new List<string>();var checks=new List<string>();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var catalog=new PlayerCatalog([new ApplicationPlayerProvider(new ApplicationCatalog(MotionPlugins.Catalog()),graphics).Contribution()]);
            for(var cycle=0;cycle<2;cycle++)
            {
                await using var lease=await catalog.OpenAsync(source,new(source));var window=(ApplicationWindow)lease.Window;
                window.Show();await window.Ready;
                await Until(()=>window.Error!=null || window.Viewport?.Frames>0,"No graphical frame");
                if(window.Error!=null)throw new Exception(window.Error);
                var state=window.Instance!.Service<MotionState>("motion.state");
                SendMessage(window.Viewport!.NativeHandle,0x100,0x27,0);
                await Until(()=>state.Snapshot.Reached || window.Error!=null,"Native keyboard did not reach the sample target");
                SendMessage(window.Viewport.NativeHandle,0x101,0x27,0);
                await window.Pump!.SetPausedAsync(true);window.Viewport.SaveImage(output+"."+cycle+".png");
                var closed=false;window.Closed+=(_,_)=>closed=true;window.Close();await Until(()=>closed,"Window did not close");
                if(window.Error!=null)throw new Exception(window.Error);
                checks.Add("native-input-target-render-close-"+cycle);
            }
            // Avalonia destroys native child windows on a later dispatcher turn after Closed.
            await Until(()=>WindowsDxBackend.LiveDevices==0 && WindowsDxBackend.LiveBackends==0 && PlatformViewport.LiveNativeWindows==0,"Graphics resources retained after window disposal");
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            if(WindowsDxBackend.LiveDevices!=0 || PlatformViewport.LiveNativeWindows!=0)errors.Add("Graphics resources retained");
            File.WriteAllText(output,JsonSerializer.Serialize(new{success=errors.Count==0,errors,checks,devices=WindowsDxBackend.LiveDevices,nativeWindows=PlatformViewport.LiveNativeWindows}));
            launcher.Close();desktop.Shutdown(errors.Count==0?0:1);
        }
    }
    [DllImport("user32.dll")]private static extern nint SendMessage(nint window,uint message,nuint wParam,nint lParam);
}
