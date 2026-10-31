using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Runtime;

namespace BoardGame.Player;

/// <summary>Fixed-content desktop entry. No selector, debug window or command-line dispatch.</summary>
public sealed class NativeApplication:Avalonia.Application
{
    private static Func<Window>? create;
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow=(create ?? throw new InvalidOperationException("Missing game window factory"))();
            var icon=Path.Combine(AppContext.BaseDirectory,"game.ico");
            if(File.Exists(icon))desktop.MainWindow.Icon=new WindowIcon(icon);
        }
        base.OnFrameworkInitializationCompleted();
    }
    public static int Run(Func<Window> window)
    {
        if(create!=null)throw new InvalidOperationException("A native application is already active");
        create=window;
        try{return AppBuilder.Configure<NativeApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);}
        finally{create=null;}
    }
    public static GraphicsConfiguration Graphics(ApplicationCatalog catalog,GameContent content)
    {
        var selected=content.Identity.Startup.Plugins;
        var definitions=catalog.Content.Plugins.Where(p=>p.Execution.Scope==RuntimeScope.GraphicsHost && selected.Any(s=>s.Id==p.Id)).ToArray();
        var startup=content.Identity.Startup with {Plugins=selected.Where(s=>definitions.Any(d=>d.Id==s.Id)).ToList()};
        RuntimeHost.Validate(startup,definitions,[RenderingPlugin.Capability],HostMode.Graphical);
        return new(startup,definitions);
    }
}

public static class NativeApplicationEntry
{
    public static int Run(ApprovedApplicationPlugin[] plugins,string source,string title)
    {
        var catalog=new ApplicationCatalog(plugins);var content=catalog.Load(source);
        var graphics=NativeApplication.Graphics(catalog,content);
        return NativeApplication.Run(()=>new ApplicationWindow(catalog,graphics,source){Title=title});
    }
}
