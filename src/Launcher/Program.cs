using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using BoardGame.Launcher;

internal static class Program
{
    internal static string? Home;
    internal static string? Verification;
    [STAThread] public static int Main(string[] args)
    {
        for(var i=0;i<args.Length;i++)
        {
            if(args[i]=="--home" && i+1<args.Length) Home=args[++i];
            else if(args[i]=="--verify-launcher" && i+1<args.Length) Verification=Path.GetFullPath(args[++i]);
            else throw new ArgumentException("未知启动器参数："+args[i]);
        }
        return AppBuilder.Configure<LauncherApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class LauncherApp:Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow=new LauncherWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class LauncherWindow:Window
{
    private readonly InstallationRegistry registry=null!;
    private readonly StackPanel installations=new(){Spacing=12};
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};
    private readonly TextBox location=new(){PlaceholderText="已解压工具包的完整目录",MinWidth=420};
    private readonly Button refresh=new(){Content="校验本地版本"};
    private readonly StackPanel releases=new(){Spacing=12};
    private readonly TextBlock catalogNotice=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};
    private readonly Button refreshCatalog=new(){Content="刷新 GitHub 版本"};
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(45)};
    public LauncherWindow()
    {
        Title="Board Game Builder · 版本管理"; Width=1040; Height=720; MinWidth=780; MinHeight=520;
        try { registry=new(Program.Home); }
        catch(Exception e) { Content=new TextBlock { Margin=new Thickness(32),Text="无法读取启动器设置，请保留设置文件并检查：\n"+e.Message,TextWrapping=TextWrapping.Wrap }; return; }
        var content=new StackPanel { Margin=new Thickness(32),Spacing=20 };
        content.Children.Add(new TextBlock { Text="BOARD GAME BUILDER",FontSize=12,LetterSpacing=2,Foreground=Brushes.LightSteelBlue });
        content.Children.Add(new TextBlock { Text="你的引擎版本",FontSize=30,FontWeight=FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text="每个版本保留自己的工具、文档和运行依赖。工程保存在你选择的目录。",TextWrapping=TextWrapping.Wrap });
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10 };
        var register=new Button { Content="登记已有工具包" };
        toolbar.Children.Add(location); toolbar.Children.Add(register); toolbar.Children.Add(refresh); content.Children.Add(toolbar);
        content.Children.Add(notice); content.Children.Add(installations);
        content.Children.Add(new TextBlock {Text="官方发行版本",FontSize=26});
        content.Children.Add(refreshCatalog); content.Children.Add(catalogNotice); content.Children.Add(releases);
        Content=new ScrollViewer { Content=content };
        refresh.Click+=async (_,_)=>await Reload();
        refreshCatalog.Click+=async (_,_)=>await LoadCatalog(true);
        Closed+=(_,_)=>http.Dispose();
        register.Click+=async (_,_)=> { try { register.IsEnabled=false; await Task.Run(()=>registry.Register(location.Text??"")); await Reload(); } catch(Exception e) { notice.Text=e.Message; } finally { register.IsEnabled=true; } };
        Opened+=async (_,_)=>
        {
            await Reload();
            await LoadCatalog(false);
            if(Program.Verification is { } report)
            {
                try
                {
                    await Task.Delay(200);
                    var statuses=await Task.Run(()=>registry.Inspect());
                    if(installations.Children.Count!=statuses.Count) throw new InvalidOperationException("安装列表未正确呈现。");
                    if(releases.Children.Count!=new ReleaseCatalog(http,registry.Home).ReadCache().Versions.Length) throw new InvalidOperationException("缓存发行列表未正确呈现。");
                    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                    using var screenshot=new Avalonia.Media.Imaging.RenderTargetBitmap(new(1040,720));
                    screenshot.Render(this); screenshot.Save(report+".png",Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new {success=true,installations=statuses.Count,catalogVersions=releases.Children.Count,settings=registry.Home,states=statuses.Select(s=>s.State)}));
                }
                catch(Exception e) { File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new {success=false,error=e.ToString()})); }
                Close();
            }
        };
    }
    private async Task LoadCatalog(bool online)
    {
        refreshCatalog.IsEnabled=false;
        try
        {
            var catalog=new ReleaseCatalog(http,registry.Home);
            if(online) catalogNotice.Text="正在分页读取官方 Release 与依赖清单…";
            var snapshot=online?await catalog.Refresh():catalog.ReadCache();
            catalogNotice.Text=(snapshot.Cached?"缓存列表":"在线列表")+" · "+(snapshot.CheckedAt==DateTimeOffset.MinValue?"尚未刷新":snapshot.CheckedAt.ToLocalTime().ToString("g"))+(snapshot.Diagnostic==null?"":"\n"+snapshot.Diagnostic);
            releases.Children.Clear(); var settings=registry.Read();
            foreach(var version in snapshot.Versions)
            {
                var row=new StackPanel {Spacing=8};
                row.Children.Add(new TextBlock {Text="V"+version.Version+(settings.Installations.Any(i=>i.Version==version.Version)?" · 已登记":""),FontSize=21});
                if(version.Diagnostic!=null) row.Children.Add(new TextBlock {Text=version.Diagnostic,Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap});
                foreach(var package in version.Packages)
                {
                    var manifest=package.ReadManifest();
                    var frameworks=manifest.Json.TryGetProperty("programs",out var programs)?string.Join("；",programs.EnumerateArray().SelectMany(p=>p.GetProperty("frameworks").EnumerateArray()).Select(f=>f.GetProperty("framework").GetString()+" "+f.GetProperty("minimumVersion").GetString()+"（同次版本补丁兼容）").Distinct()):"旧完整包自带运行依赖";
                    row.Children.Add(new TextBlock {Text=(package.Mode switch {"standalone"=>"独立包／私有 Runtime", "slim"=>"精简包／系统 Runtime",_=>"旧完整包"})+" · "+(package.Archive.Size/1048576d).ToString("F1")+" MiB · "+manifest.Components.Count+" 个组件\n"+frameworks,TextWrapping=TextWrapping.Wrap});
                }
                row.Children.Add(new Expander {Header="发布说明",Content=new TextBlock {Text=version.Notes,TextWrapping=TextWrapping.Wrap}});
                releases.Children.Add(new Border {Padding=new Thickness(20),Background=new SolidColorBrush(Color.Parse("#202832")),CornerRadius=new CornerRadius(10),Child=row});
            }
        }
        catch(Exception e) {catalogNotice.Text="版本列表不可用："+e.Message;}
        finally {refreshCatalog.IsEnabled=true;}
    }
    private async Task Reload()
    {
        refresh.IsEnabled=false; notice.Text="正在核对版本清单和文件…";
        try
        {
            var status=await Task.Run(()=>registry.Inspect()); var settings=registry.Read(); installations.Children.Clear();
            foreach(var item in status)
            {
                var row=new StackPanel { Spacing=8 };
                row.Children.Add(new TextBlock { Text="V"+item.Installation.Version+(settings.DefaultVersion==item.Installation.Version?"  ·  默认版本":""),FontSize=22 });
                row.Children.Add(new TextBlock { Text=(item.Installation.Mode switch { "standalone"=>"私有运行时", "slim"=>"系统运行时", _=>"旧完整包" })+"  ·  "+(item.State switch { "available"=>"可用", "missing"=>"目录缺失", _=>"需要修复" }),Foreground=item.State=="available"?Brushes.LightGreen:Brushes.Orange });
                row.Children.Add(new TextBlock { Text=item.Installation.Directory,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray });
                if(item.Diagnostic!=null) row.Children.Add(new TextBlock { Text=item.Diagnostic,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Orange });
                var choose=new Button { Content="设为默认版本",IsEnabled=item.State=="available" };
                choose.Click+=async (_,_)=> { try { choose.IsEnabled=false; await Task.Run(()=>registry.SetDefault(item.Installation.Version)); await Reload(); } catch(Exception e) { notice.Text=e.Message; } finally { choose.IsEnabled=true; } };
                row.Children.Add(choose); installations.Children.Add(new Border { Padding=new Thickness(20),Background=new SolidColorBrush(Color.Parse("#202832")),CornerRadius=new CornerRadius(10),Child=row });
            }
            notice.Text=status.Count==0?"还没有登记的引擎。可先解压官方工具包，再填写目录进行登记。":"已校验 "+status.Count+" 个版本。设置保存在 "+registry.Home;
        }
        catch(Exception e) { notice.Text="无法读取安装登记："+e.Message; }
        finally { refresh.IsEnabled=true; }
    }
}
