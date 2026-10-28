using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Runtime;
using Sample.Motion;

Probe.Output=Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/application-loop/motion-view.json");
AppBuilder.Configure<Probe>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
return Probe.Failed?1:0;

public sealed class Probe : Avalonia.Application
{
    public static string Output="";public static bool Failed;
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window=new Window{Title="Generic application verification",Width=720,Height=450};
            desktop.MainWindow=window;desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window.Opened+=async(_,_)=>await Run(window,desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Until(Func<bool> ready,string message)
    {for(var wait=0;wait<300;wait++){if(ready())return;await Task.Delay(30);}throw new Exception(message);}
    private static async Task Run(Window standalone,IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errors=new List<string>();var checks=new List<string>();var screenshots=new List<string>();
        RuntimeHost? instance=null;ApplicationPump? pump=null;Window? embedded=null;
        ApplicationViewport? one=null,two=null,three=null;
        try
        {
            var catalog=new ApplicationCatalog(MotionPlugins.Catalog());var content=catalog.Load("samples/motion-lab");
            instance=await catalog.StartAsync(content,clock:new(TimeSpan.FromMilliseconds(10),20));
            var state=instance.Service<MotionState>("motion.state");
            var backend=WindowsDxBackend.Plugin();var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
            one=new(graphics,instance);standalone.Content=one;
            two=new(graphics,instance);three=new(graphics,instance);
            var grid=new Grid{ColumnDefinitions=new("*,*")};grid.Children.Add(two);Grid.SetColumn(three,1);grid.Children.Add(three);
            var dock=new DockPanel();var title=new TextBlock{Text="Embedded application — shared update, independent viewports",Margin=new Thickness(8)};
            DockPanel.SetDock(title,Dock.Top);dock.Children.Add(title);dock.Children.Add(grid);
            embedded=new Window{Title="Embedded host verification",Width=1000,Height=430,Content=dock};embedded.Show();
            foreach(var view in new[]{one,two,three})view.ApplicationError+=errors.Add;
            await Until(()=>one.Frames>0 && two.Frames>0 && three.Frames>0 || errors.Count>0,"No frames");
            Check(errors.Count==0,string.Join("; ",errors));Check(WindowsDxBackend.LiveDevices==3 && WindowsDxBackend.LiveBackends==2,"Viewport/backend scope differs");
            checks.Add("standalone-and-two-embedded-viewports");
            pump=new(instance.Driver);
            SendMessage(one.NativeHandle,0x100,0x27,0); // native Right key down
            await Until(()=>state.Snapshot.Reached || errors.Count>0,"Continuous keyboard movement never reached target");
            SendMessage(one.NativeHandle,0x101,0x27,0);
            Check(errors.Count==0,string.Join("; ",errors));
            await pump.SetPausedAsync(true);var paused=state.Snapshot;var frames=two.Frames;
            await Until(()=>two.Frames>frames+3,"Paused render stopped");Check(state.Snapshot==paused,"Render changed paused simulation");
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            one.SaveImage(Output+".standalone.png");two.SaveImage(Output+".embedded.png");
            screenshots.AddRange([Output+".standalone.png",Output+".embedded.png"]);checks.Add("native-key-down-up-continuous-motion-target-and-paused-render");
            SendMessage(two.NativeHandle,0x100,0x20,0);SendMessage(two.NativeHandle,0x101,0x20,0);
            await pump.StepAsync();Check(Math.Abs(state.Snapshot.X-60)<.001 && !state.Snapshot.Reached,"Reset did not use one update");
            SendMessage(two.NativeHandle,0x100,0x27,0);var before=state.Snapshot;
            await pump.StepAsync();Check(state.Snapshot.Tick==before.Tick+1 && Math.Abs(state.Snapshot.X-61.4)<.01,"Multiple views advanced duplicate updates");
            one.Dispose();standalone.Content=null;standalone.Close();await one.Shutdown;
            await pump.SetPausedAsync(false);await Until(()=>state.Snapshot.X>90,"Closing one view ended shared instance");
            SendMessage(two.NativeHandle,0x8,0,0); // native focus loss
            await Task.Delay(100);await pump.SetPausedAsync(true);var stopped=state.Snapshot;
            await pump.StepAsync();Check(state.Snapshot.X==stopped.X,"Focus loss retained movement");
            checks.Add("single-step-shared-instance-close-one-view-and-focus-cancel");
            var deps=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Motion.ViewTests.deps.json"));
            Check(!deps.Contains("Kernel.Abstractions/") && !deps.Contains("BoardGame.Core/") && !deps.Contains("View.MonoGame/"),"Graphical host links board implementation");
            for(var cycle=0;cycle<2;cycle++)
            {
                var app=new ApplicationWindow(catalog,graphics,"samples/motion-lab");app.Show();await app.Ready;
                await Until(()=>app.Viewport?.Frames>0 || app.Error!=null,"Application window did not start");
                Check(app.Error==null,app.Error ?? "");
                var closed=false;app.Closed+=(_,_)=>closed=true;app.Close();
                await Until(()=>closed,"Application window did not close");Check(app.Error==null,app.Error ?? "");
            }
            var pending=new ApplicationWindow(catalog,graphics,"samples/motion-lab");pending.Show();
            var pendingClosed=false;pending.Closed+=(_,_)=>pendingClosed=true;pending.Close();
            await Until(()=>pendingClosed,"Closing during load leaked window");
            checks.Add("application-window-repeated-start-close-and-close-during-load");
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            if(pump!=null)try{await pump.DisposeAsync();}catch(Exception error){errors.Add(error.Message);}
            foreach(var view in new[]{one,two,three})view?.Dispose();
            standalone.Close();embedded?.Close();
            foreach(var view in new[]{one,two,three})if(view!=null)await view.Shutdown;
            if(instance!=null)await instance.DisposeAsync();
            for(var wait=0;PlatformViewport.LiveNativeWindows>0 && wait<100;wait++)await Task.Delay(20);
            if(WindowsDxBackend.LiveDevices!=0 || WindowsDxBackend.LiveBackends!=0 || PlatformViewport.LiveNativeWindows!=0)errors.Add("Resources retained");
            Failed=errors.Count>0;
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            File.WriteAllText(Output,JsonSerializer.Serialize(new{success=!Failed,errors,checks,screenshots,devices=WindowsDxBackend.LiveDevices,backends=WindowsDxBackend.LiveBackends,nativeWindows=PlatformViewport.LiveNativeWindows}));
            desktop.Shutdown(Failed?1:0);
        }
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window,uint message,nuint wParam,nint lParam);
}
