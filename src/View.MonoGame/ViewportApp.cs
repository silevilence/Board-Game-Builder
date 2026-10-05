using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
namespace BoardGame.View;

public sealed class ViewportApp : Application
{
    public static Func<Window>? WindowFactory { get; set; }
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow=WindowFactory!();
        base.OnFrameworkInitializationCompleted();
    }
    public static int Run(string title, string[] args, Func<Window>? factory=null)
    {
        WindowFactory=factory ?? (() => CreateProbe(title,args));
        return AppBuilder.Configure<ViewportApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
    private static Window CreateProbe(string title,string[] args)
    {
        var host=new ContentControl { Content=new DesktopViewport() };
        var status=new TextBlock { Text="Wheel zoom / right drag pan / click. Resize window; rebuild to test disposal." };
        var rebuild=new Button { Content="Rebuild viewport" };
        rebuild.Click+=(_,_) => { (host.Content as DesktopViewport)?.Dispose(); host.Content=null; host.Content=new DesktopViewport(); status.Text=$"Live graphics devices: {DesktopViewport.LiveDevices}"; };
        var panel=new DockPanel(); DockPanel.SetDock(rebuild,Dock.Top); DockPanel.SetDock(status,Dock.Top);
        panel.Children.Add(rebuild); panel.Children.Add(status); panel.Children.Add(host);
        var window=new Window { Title=title, Width=960, Height=640, Content=panel };
        window.Closed+=(_,_) => (host.Content as DesktopViewport)?.Dispose();
        if(args.Length==2 && args[0]=="--smoke")
        {
            var count=0; var failures=new List<string>();
            var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
            timer.Tick+=(_,_) =>
            {
                if(host.Content is DesktopViewport view && (view.Frames==0 || view.Error!=null)) failures.Add(view.Error ?? "No rendered frames");
                (host.Content as DesktopViewport)?.Dispose(); host.Content=null;
                if(DesktopViewport.LiveDevices!=0) failures.Add("Graphics device leaked");
                if(++count==3)
                {
                    timer.Stop(); File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new { title,cycles=count,failures }));
                    window.Close();
                }
                else { window.Width+=40; window.Height+=20; host.Content=new DesktopViewport(); }
            };
            window.Opened+=(_,_) => timer.Start(); window.Closed+=(_,_) => timer.Stop();
        }
        return window;
    }
}
