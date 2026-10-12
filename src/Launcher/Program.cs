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
    private readonly Button cancelInstall=new(){Content="取消安装",IsEnabled=false};
    private CancellationTokenSource? installationLifetime;
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
        var import=new Button {Content="导入本地 ZIP"}; var maintenance=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};
        maintenance.Children.Add(import);maintenance.Children.Add(cancelInstall);content.Children.Add(maintenance);
        cancelInstall.Click+=(_,_)=>installationLifetime?.Cancel();
        import.Click+=async (_,_)=>
        {
            var files=await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions {Title="选择官方工具包 ZIP（同目录须有 .sha256）",AllowMultiple=false,FileTypeFilter=[new("工具包 ZIP"){Patterns=["*.zip"]}]});
            if(files.Count==0) return;
            var path=files[0].Path.LocalPath;
            await Install((service,progress,cancellation)=>service.Import(path,path+".sha256",progress,cancellation));
        };
        content.Children.Add(notice); content.Children.Add(installations);
        content.Children.Add(new TextBlock {Text="官方发行版本",FontSize=26});
        content.Children.Add(refreshCatalog); content.Children.Add(catalogNotice); content.Children.Add(releases);
        Content=new ScrollViewer { Content=content };
        refresh.Click+=async (_,_)=>await Reload();
        refreshCatalog.Click+=async (_,_)=>await LoadCatalog(true);
        Closed+=(_,_)=> {installationLifetime?.Cancel();http.Dispose();};
        register.Click+=async (_,_)=> { try { register.IsEnabled=false; await Task.Run(()=>registry.Register(location.Text??"")); await Reload(); } catch(Exception e) { notice.Text=e.Message; } finally { register.IsEnabled=true; } };
        Opened+=async (_,_)=>
        {
            string? recoveryFailure=null;
            try {await Task.Run(()=>new InstallationService(registry,http).Recover());} catch(Exception e) {recoveryFailure=e.Message;}
            await Reload();
            if(recoveryFailure!=null) notice.Text="未完成的安装需要处理："+recoveryFailure;
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
    private async Task Install(Func<InstallationService,IProgress<TransferProgress>,CancellationToken,Task<InstallationResult>> action)
    {
        if(installationLifetime!=null) {notice.Text="已有安装操作正在进行。";return;}
        using var lifetime=new CancellationTokenSource(); installationLifetime=lifetime;cancelInstall.IsEnabled=true;
        var progress=new Progress<TransferProgress>(p=>notice.Text=(p.Stage switch {"download"=>"下载组件","verify"=>"校验文件",_=>"安装版本"})+" · "+(p.DownloadedBytes/1048576d).ToString("F1")+" MiB / "+(p.TotalBytes/1048576d).ToString("F1")+" MiB");
        try
        {
            var result=await Task.Run(()=>action(new InstallationService(registry,http),progress,lifetime.Token));
            await Reload();await LoadCatalog(false);notice.Text="V"+result.Installation.Version+" 安装完成；传输 "+(result.DownloadedBytes/1048576d).ToString("F1")+" MiB，复用 "+result.ReusedObjects+" 个内容对象。";
        }
        catch(OperationCanceledException) {notice.Text="安装已取消，原有版本保留。";}
        catch(Exception e) {notice.Text="安装未完成："+e.Message;}
        finally {installationLifetime=null;cancelInstall.IsEnabled=false;}
    }
    private async Task Maintain(Func<CancellationToken,Task<string>> action)
    {
        if(installationLifetime!=null) {notice.Text="已有安装或维护操作正在进行。";return;}
        using var lifetime=new CancellationTokenSource();installationLifetime=lifetime;cancelInstall.IsEnabled=true;cancelInstall.Content="取消维护";
        try {notice.Text="正在检查运行占用和共享引用…";var message=await Task.Run(()=>action(lifetime.Token));await Reload();await LoadCatalog(false);notice.Text=message;}
        catch(OperationCanceledException) {notice.Text="维护已取消。";}
        catch(Exception e) {notice.Text="维护未完成："+e.Message;}
        finally {installationLifetime=null;cancelInstall.IsEnabled=false;cancelInstall.Content="取消安装";}
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
                    var checkRuntime=new Button {Content=package.Mode=="slim"?"检查系统运行时模式":"检查私有运行时模式"};
                    checkRuntime.Click+=async (_,_)=> {try {checkRuntime.IsEnabled=false; var resolution=await Task.Run(()=>RuntimePolicy.Resolve(RuntimePolicy.Select(version,package.Mode=="slim"?"system":"private").ReadManifest())); catalogNotice.Text="V"+version.Version+" · "+resolution.Mode+"\n"+string.Join("\n",resolution.Programs.SelectMany(p=>p.Frameworks).Distinct().Select(f=>f.Framework+" "+f.ActualVersion+" · "+f.Policy+" · "+(f.Location??"按固定组件安装")));} catch(Exception e) {catalogNotice.Text=e.Message;} finally {checkRuntime.IsEnabled=true;} };
                    row.Children.Add(checkRuntime);
                    var install=new Button {Content=package.Mode=="slim"?"安装 · 系统运行时":"安装 · 私有运行时",IsEnabled=package.Mode!="legacy"&&!settings.Installations.Any(i=>i.Version==version.Version)};
                    install.Click+=async (_,_)=>await Install((service,progress,cancellation)=>service.Install(version,package.Mode=="slim"?"system":"private",progress,cancellation));
                    row.Children.Add(install);
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
                var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};actions.Children.Add(choose);row.Children.Add(actions);
                installations.Children.Add(new Border { Padding=new Thickness(20),Background=new SolidColorBrush(Color.Parse("#202832")),CornerRadius=new CornerRadius(10),Child=row });
                var runtime=new Button {Content="查看实际运行时"};
                runtime.Click+=async (_,_)=> {try {runtime.IsEnabled=false; var resolution=await Task.Run(()=>RuntimePolicy.Inspect(registry,item.Installation.Version)); notice.Text="V"+resolution.Version+" · "+resolution.Mode+"\n"+string.Join("\n",resolution.Programs.SelectMany(p=>p.Frameworks).Distinct().Select(f=>f.Framework+" "+f.ActualVersion+" · "+f.Policy+" · "+f.Location));} catch(Exception e) {notice.Text=e.Message;} finally {runtime.IsEnabled=true;} };
                actions.Children.Add(runtime);
                var launch=new Button {Content="启动 Editor",IsEnabled=item.State=="available"};
                launch.Click+=async (_,_)=> {try {launch.IsEnabled=false;using var process=await Task.Run(()=>new ToolExecution(registry).Start(item.Installation.Version,"editor",[]));notice.Text="已启动 V"+item.Installation.Version+"，进程 "+process.Id;} catch(Exception e) {notice.Text=e.Message;} finally {launch.IsEnabled=true;} };actions.Children.Add(launch);
                var repair=new Button {Content="修复",IsEnabled=item.Installation.Managed&&item.Installation.Mode!="legacy"};
                repair.Click+=async (_,_)=>await Maintain(async cancellation=> {var result=await new InstallationRepair(registry,http).Repair(item.Installation.Version,cancellation:cancellation);return "已修复 "+string.Join("、",result.Versions.Select(v=>"V"+v))+"，替换 "+result.ReplacedObjects+" 个共享对象。";});actions.Children.Add(repair);
                var remove=new Button {Content=item.Installation.Managed?"卸载":"移除登记"};
                remove.Click+=async (_,_)=>await Maintain(async cancellation=> {var result=await new InstallationMaintenance(registry).Uninstall(item.Installation.Version,cancellation);return "V"+result.Version+(result.DirectoryRemoved?" 已卸载。":" 已取消登记，原目录保留。")+(result.ChooseDefault?"请重新选择默认版本。":"");});actions.Children.Add(remove);
            }
            notice.Text=status.Count==0?"还没有登记的引擎。可先解压官方工具包，再填写目录进行登记。":"已校验 "+status.Count+" 个版本。设置保存在 "+registry.Home;
            if(status.Count>0&&settings.DefaultVersion==null) notice.Text+="。请重新选择默认版本。";
        }
        catch(Exception e) { notice.Text="无法读取安装登记："+e.Message; }
        finally { refresh.IsEnabled=true; }
    }
}
