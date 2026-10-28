using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using Sample.Motion;

MotionApp.Source=Path.GetFullPath(args.FirstOrDefault() ?? "samples/motion-lab");
AppBuilder.Configure<MotionApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);

public sealed class MotionApp : Avalonia.Application
{
    public static string Source="";
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var backend=WindowsDxBackend.Plugin();
            var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
            desktop.MainWindow=new ApplicationWindow(new ApplicationCatalog(MotionPlugins.Catalog()),graphics,Source);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
