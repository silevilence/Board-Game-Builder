using Avalonia.Controls;
using Avalonia.Layout;
using BoardGame.Plugins;

namespace BoardGame.Editor;

public sealed class RuntimePluginManagementPanel:UserControl,IDisposable
{
    private readonly RuntimePluginManagement management;
    private readonly RuntimeConfiguration configuration;
    private readonly DocumentWorkspace documents;
    private readonly EditorCommands commands;
    private readonly ListBox packages=new(){MinHeight=160,MaxHeight=260};
    private readonly TextBox source=new(){PlaceholderText="包含 runtime.plugin.json 的发布者包目录"};
    private readonly TextBlock details=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBlock status=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBlock project=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button import=new(){Content="导入新实现"},replace=new(){Content="显式替换同一版本"},remove=new(){Content="移除选中版本"},enable=new(){Content="写入当前工程草稿"},cancel=new(){Content="取消操作"};
    private bool refreshing;
    private sealed record Choice(PluginManifest Manifest){public override string ToString()=>$"{Manifest.Id}  {Manifest.Version} / API {Manifest.Api}";}
    public RuntimePluginManagementPanel(EditorPluginScope scope)
    {
        management=scope.Service<RuntimePluginManagement>("runtime.management");configuration=scope.Service<RuntimeConfiguration>("runtime.configuration");documents=scope.Service<DocumentWorkspace>("documents");
        commands=scope.Service<EditorCommands>("commands");
        source.Text=management.PackagePath;source.TextChanged+=(_,_)=>{if(!refreshing){management.PackagePath=source.Text ?? "";commands.Refresh();}};
        import.Click+=async(_,_)=>await commands.Execute("runtime.plugins.import");replace.Click+=async(_,_)=>await commands.Execute("runtime.plugins.replace");
        remove.Click+=async(_,_)=>await commands.Execute("runtime.plugins.remove");
        enable.Click+=async(_,_)=>
        {
            try
            {
                await commands.Execute("runtime.plugins.enable");
                status.Text="已写入当前工程草稿，可撤销；安装目录未改变。";
            }
            catch(Exception error){status.Text="工程选择失败："+error.Message;}
        };
        cancel.Click+=async(_,_)=>await commands.Execute("runtime.plugins.cancel");
        var refresh=new Button{Content="重新校验安装目录"};refresh.Click+=(_,_)=>management.Refresh();packages.SelectionChanged+=(_,_)=>{if(!refreshing){management.Selection=(packages.SelectedItem as Choice)?.Manifest;commands.Refresh();Describe();}};
        Content=new ScrollViewer{Content=new StackPanel{Margin=new(20),Spacing=12,Children={
            new TextBlock{Text="外部运行插件",FontSize=20},new TextBlock{Text="此处管理游戏运行实现；Editor 扩展使用“插件管理”。导入不会启用工程插件，AOT 声明不能在此修改。",TextWrapping=Avalonia.Media.TextWrapping.Wrap},
            new TextBlock{Text="安装目录："+management.DirectoryPath,TextWrapping=Avalonia.Media.TextWrapping.Wrap},source,
            new WrapPanel{Orientation=Orientation.Horizontal,Children={import,replace,remove,enable,refresh,cancel}},status,packages,details,
            new TextBlock{Text="当前工程的身份与依赖诊断",FontSize=18},project}}};
        management.Changed+=Refresh;documents.Changed+=RefreshProject;commands.Changed+=RefreshCommands;management.Refresh();
    }
    private void Refresh()
    {
        refreshing=true;
        try
        {
            var selected=(packages.SelectedItem as Choice)?.Manifest;
            var choices=management.Installed.Select(p=>new Choice(p)).ToArray();packages.ItemsSource=choices;
            packages.SelectedItem=choices.FirstOrDefault(p=>selected!=null && p.Manifest.Id==selected.Id && p.Manifest.Version==selected.Version) ?? choices.FirstOrDefault();
            management.Selection=(packages.SelectedItem as Choice)?.Manifest;commands.Refresh();
            source.IsEnabled=!management.Busy;RefreshCommands();
            status.Text=management.Status;Describe();RefreshProject();
        }
        finally{refreshing=false;}
    }
    private void Describe()
    {
        if(packages.SelectedItem is not Choice choice){details.Text="暂无可查看的运行实现。";return;}
        var manifest=choice.Manifest;
        details.Text=$"{manifest.Id} {manifest.Version}\n实现指纹：{manifest.Fingerprint}\n平台：{string.Join(", ",manifest.Targets)}\n作用域：{manifest.Execution.Scope} / {manifest.Execution.Modes}\n"+
            "提供："+string.Join(", ",manifest.Provides.Select(p=>$"{p.Name}@{p.Api}"))+"\n依赖："+string.Join(", ",manifest.Requires.Select(p=>$"{p.Name}@{p.Api}"))+"\n"+
            "AOT："+(manifest.Aot==null?"未声明，不能进入 NativeAOT 发布":manifest.Aot.Compatible?"发布者声明兼容 "+string.Join(", ",manifest.Aot.Targets)+"；仍须依赖分析、真实编译与目标环境验证":"不兼容："+manifest.Aot.Reason)+"\n"+
            string.Join("\n",(manifest.Execution.Contributions ?? []).Select(p=>$"{p.Phase} {p.Id}；先于 [{string.Join(", ",p.Before ?? [])}]；后于 [{string.Join(", ",p.After ?? [])}]"));
    }
    private void RefreshCommands()
    {
        import.IsEnabled=commands.CanExecute("runtime.plugins.import");replace.IsEnabled=commands.CanExecute("runtime.plugins.replace");
        remove.IsEnabled=commands.CanExecute("runtime.plugins.remove");enable.IsEnabled=commands.CanExecute("runtime.plugins.enable");cancel.IsEnabled=commands.CanExecute("runtime.plugins.cancel");
    }
    private void RefreshProject(){project.Text=configuration.Describe();RefreshCommands();}
    public void Dispose(){management.Changed-=Refresh;documents.Changed-=RefreshProject;commands.Changed-=RefreshCommands;}
}
