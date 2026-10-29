using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using BoardGame.View;
using Sample.Motion;

Probe.AllSamples=args.FirstOrDefault()=="--samples";
Probe.Output=Path.GetFullPath((Probe.AllSamples?args.ElementAtOrDefault(1):args.FirstOrDefault()) ?? ".scratch/application-loop/player-host.json");
AppBuilder.Configure<Probe>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
return Probe.Failed?1:0;

public sealed class Probe : Avalonia.Application
{
    public static string Output="";public static bool Failed,AllSamples;
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var root=new Window{Title="Player capability verification",Width=400,Height=100};desktop.MainWindow=root;
            root.Opened+=async(_,_)=>await Run(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Until(Func<bool> ready,string message)
    {for(var i=0;i<400;i++){if(ready())return;await Task.Delay(25);}throw new Exception(message);}
    private static async Task Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errors=new List<string>();var checks=new List<string>();PlayerWindowLease? current=null;
        try
        {
            var backend=WindowsDxBackend.Plugin();
            var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
            var motionCatalog=new ApplicationCatalog(MotionPlugins.Catalog());
            var motion=new ApplicationPlayerProvider(motionCatalog,graphics).Contribution();var board=BoardPlayerProvider.Contribution();
            var catalog=new PlayerCatalog([board,motion]);
            if(AllSamples)
            {
                await SamplesVerification.Run(catalog,motionCatalog,Output);
                checks.Add("all-sample-directories-and-packages-render-interact-and-close");
                return;
            }
            var forbidden=new[]{"Kernel", "BoardGame.Core", "Runtime", "Player.Board", "Sample.Motion"};
            Check(!typeof(PlayerCatalog).Assembly.GetReferencedAssemblies().Any(a=>forbidden.Contains(a.Name)),"Generic Player references an implementation");
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            var package=Output+".bgm";
            if(!File.Exists(package))GamePackage.Pack("samples/motion-lab",package,motionCatalog.Content);
            foreach(var source in new[]{"samples/quick-cards","samples/motion-lab",package,"samples/quick-cards"})
            {
                current=await catalog.OpenAsync(source,new(source));current.Window.Show();
                if(current.Window is ApplicationWindow application)
                {
                    await application.Ready;await Until(()=>application.Viewport?.Frames>2 || application.Error!=null,"Realtime Player failed to render");
                    Check(application.Error==null,application.Error ?? "");
                    Check(!application.GetLogicalDescendants().OfType<ComboBox>().Any(),"Realtime game forced seat selection");
                    application.Viewport!.Submit(new("key-down",Key:"right"));
                    var state=application.Instance!.Service<MotionState>("motion.state");
                    await Until(()=>state.Snapshot.X>80,"Realtime Player failed to update");
                }
                else
                {
                    PlayBoard? play=null;await Until(()=>(play=current.Window.GetLogicalDescendants().OfType<PlayBoard>().FirstOrDefault())!=null,"Board Player failed to start");
                    await Until(()=>play!.Frame.Actions.Count>0 || play.Frame.View.Ended || play.Frame.Fault!=null,"Board Player never reached input");
                    Check(play!.Frame.Fault==null,"Board Player faulted");
                }
                await current.DisposeAsync();current=null;
                await Until(()=>WindowsDxBackend.LiveDevices==0 && PlatformViewport.LiveNativeWindows==0,"Player resources retained");
            }
            checks.Add("same-player-board-realtime-package-and-repeat-no-seat-ui");
            var rejected=false;try {current=await new PlayerCatalog([board]).OpenAsync("samples/motion-lab",new());}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Unknown capability accepted");
            rejected=false;try {current=await new PlayerCatalog([motion,new ApplicationPlayerProvider(motionCatalog,graphics).Contribution("player.other")]).OpenAsync("samples/motion-lab",new());}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Ambiguous capability accepted");checks.Add("unknown-and-ambiguous-provider-rejected");
            current=await catalog.OpenAsync("samples/motion-lab",new());await current.DisposeAsync();current=null;
            checks.Add("unshown-cancelled-window-releases-provider");
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            if(current!=null)await current.DisposeAsync();
            Failed=errors.Count>0;File.WriteAllText(Output,JsonSerializer.Serialize(new{success=!Failed,errors,checks,devices=WindowsDxBackend.LiveDevices,backends=WindowsDxBackend.LiveBackends,nativeWindows=PlatformViewport.LiveNativeWindows}));
            desktop.Shutdown(Failed?1:0);
        }
    }
}
