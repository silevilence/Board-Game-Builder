using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed partial class EditorWindow : Window
{
    private ModuleDocument? document;
    private readonly TextBox location=new() { Width=360,Watermark="模块目录（另存为请选择新目录）" };
    private readonly TextBox source=new() { AcceptsReturn=true,FontFamily=FontFamily.Parse("Consolas"),TextWrapping=TextWrapping.NoWrap };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8) };
    private readonly TabControl tabs=new();
    private readonly WrapPanel toolbar=new() { Margin=new Thickness(6) };
    private bool allowClose;
    private string sourceBase="";
    private readonly PlayPanel play;
    public EditorWindow(string? initial=null)
    {
        Title="Board Game Builder · Editor"; Width=1400; Height=900;
        play=new PlayPanel(()=>document); scriptReload=play.ReloadScripts;
        toolbar.Children.Add(location);
        Add("新建",async ()=> { if(await CanLeave()) SetDocument(ModuleDocument.Create()); });
        Add("打开",async ()=> { if(await CanLeave()) SetDocument(ModuleDocument.Open(location.Text ?? "")); });
        Add("保存",()=>Run(()=> { ApplySource(); document?.Save(location.Text); }));
        Add("关闭文档",async ()=> { if(await CanLeave()) SetDocument(null); });
        Add("撤销",()=>Run(()=>document?.Undo())); Add("重做",()=>Run(()=>document?.Redo()));
        Add("应用配置",()=>Run(ApplySource));
        Add("放弃配置草稿并重新读取",()=>Run(()=> { sourceBase=document?.Text("module.json") ?? ""; source.Text=sourceBase; }));
        tabs.Items.Add(new TabItem { Header="模块配置",Content=new ScrollViewer {Content=source} });
        tabs.Items.Add(new TabItem {Header="试玩",Content=play});
        tabs.Items.Add(new TabItem {Header="独立导出",Content=new ExportPanel(()=> {ApplySource();return document;},plugins)});
        Closed+=(_,_)=>play.Dispose();
        var root=new DockPanel(); DockPanel.SetDock(toolbar,Dock.Top); DockPanel.SetDock(status,Dock.Bottom);
        root.Children.Add(toolbar); root.Children.Add(status); root.Children.Add(tabs); Content=root;
        InitializePlugins();
        source.TextChanged+=(_,_)=>UpdateTitle();
        Closing+=async (_,e)=> { if(allowClose) return; e.Cancel=true; try { if(await CanLeave()) { allowClose=true; Close(); } } catch(Exception error) { status.Text=error.Message; } };
        if(initial!=null) { try { SetDocument(ModuleDocument.Open(initial)); } catch(Exception e) { status.Text=e.Message; } }
        else SetDocument(ModuleDocument.Create());
    }
    private Task Run(Action action) { try { action(); status.Text="完成"; } catch(Exception e) { status.Text=e.Message; } return Task.CompletedTask; }
    private void Add(string text,Func<Task> action)
    {
        var button=new Button {Content=text,Margin=new Thickness(3)};
        button.Click+=async (_,_)=> { try { await action(); } catch(Exception e) { status.Text=e.Message; } }; toolbar.Children.Add(button);
    }
    private bool BufferDirty => document!=null && source.Text!=sourceBase;
    private void ApplySource()
    {
        if(document==null || !BufferDirty) return;
        if(document.Text("module.json")!=sourceBase && source.Text!=document.Text("module.json"))
            throw new InvalidOperationException("其他面板已修改配置；JSON 草稿仍保留。请复制草稿后重新读取最新配置并合并修改。");
        _=System.Text.Json.Nodes.JsonNode.Parse(source.Text ?? "")!.AsObject();
        document.EditText("module.json",source.Text ?? ""); sourceBase=source.Text ?? ""; UpdateTitle();
    }
    private void SetDocument(ModuleDocument? value)
    {
        if(document!=null) document.Changed-=RefreshDocument;
        document=value;
        sourceBase=value?.Text("module.json") ?? ""; source.Text=sourceBase;
        play.DocumentChanged();
        plugins.SetDocument(value);
        location.Text=value?.DirectoryPath;
        if(document!=null) document.Changed+=RefreshDocument;
        RefreshDocument();
    }
    private void RefreshDocument() { if(!BufferDirty) { sourceBase=document?.Text("module.json") ?? ""; source.Text=sourceBase; } UpdateTitle(); }
    private void UpdateTitle() => Title="Board Game Builder · Editor"+((document?.Dirty==true || BufferDirty)?" *":"");
    private async Task<bool> CanLeave()
    {
        if(document==null || (!document.Dirty && !BufferDirty)) return true;
        var dialog=new Window {Title="未保存的模块",Width=440,Height=160,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        foreach(var (label,result) in new[] {("保存",1),("放弃",2),("取消",0)})
        { var button=new Button {Content=label}; button.Click+=(_,_)=>dialog.Close(result); buttons.Children.Add(button); }
        dialog.Content=new StackPanel {Margin=new Thickness(20),Spacing=16,Children={new TextBlock {Text="模块有未保存修改，是否保存？"},buttons}};
        var choice=await dialog.ShowDialog<int>(this);
        if(choice==1) { ApplySource(); document.Save(location.Text); }
        return choice!=0;
    }
}
