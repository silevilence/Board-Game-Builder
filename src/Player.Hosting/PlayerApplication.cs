using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;

namespace BoardGame.Player;

public sealed class PlayerApplication : Avalonia.Application
{
    private static PlayerCatalog catalog=null!;
    private static PlayerLaunch launch=null!;
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new LaunchWindow(catalog,launch,desktop);
        base.OnFrameworkInitializationCompleted();
    }
    public static int Run(PlayerCatalog installed,PlayerLaunch requested)
    {catalog=installed;launch=requested;return AppBuilder.Configure<PlayerApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);}

    private sealed class LaunchWindow : Window
    {
        private bool closed;
        public LaunchWindow(PlayerCatalog installed,PlayerLaunch requested,IClassicDesktopStyleApplicationLifetime desktop)
        {
            Title="Player";Width=680;Height=240;
            var path=new TextBox{Text=requested.Module ?? "",PlaceholderText="工程目录、game.start.json 或游戏包"};
            var message=new TextBlock{Text="选择游戏内容",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
            var open=new Button{Content="打开"};var browse=new Button{Content="选择文件"};
            Content=new StackPanel{Margin=new(24),Spacing=10,Children={path,browse,open,message}};
            Closed+=(_,_)=>closed=true;
            async Task Start()
            {
                open.IsEnabled=false;message.Text="正在加载…";
                try
                {
                    var lease=await installed.OpenAsync(path.Text ?? "",requested);
                    if(closed){await lease.DisposeAsync();return;}
                    var window=lease.Window;
                    desktop.MainWindow=window;window.Show();Close();
                }
                catch(Exception error){if(!closed){message.Text="无法打开："+error.Message;open.IsEnabled=true;}}
            }
            open.Click+=async(_,_)=>await Start();
            browse.Click+=async(_,_)=>
            {
                var selected=await StorageProvider.OpenFilePickerAsync(new(){Title="选择游戏入口或游戏包",AllowMultiple=false});
                if(!closed && selected.Count==1)path.Text=selected[0].TryGetLocalPath();
            };
            if(requested.Module!=null)Opened+=async(_,_)=>await Start();
        }
    }
}
