using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BoardGame.Editor;

public static class DocumentPlugin
{
    public static void Activate(EditorApplication app,Window window,EditorPluginScope scope)
    {
        var documents=new DocumentWorkspace();scope.Own(documents);scope.Provide("documents",documents);
        var shell=scope.Service<WorkspaceShell>("workspace");var commands=scope.Service<EditorCommands>("commands");
        var path=new TextBox {Width=400,PlaceholderText="模块目录（另存为请选择新目录）"};shell.Toolbar.Children.Add(path);
        scope.Own(new Cleanup(()=>shell.Toolbar.Children.Remove(path)));
        path.TextChanged+=(_,_)=>documents.Location=path.Text ?? "";
        void Refresh()
        {
            if(path.Text!=documents.Location) path.Text=documents.Location;
            window.Title="Board Game Builder · Editor"+(documents.Dirty?" *":"");commands.Refresh();
        }
        documents.Changed+=Refresh;scope.Own(new Cleanup(()=>documents.Changed-=Refresh));
        scope.Own(app.BeforeShutdown(()=>documents.CanLeave(()=>Choose(window))));
        Add("document.new","新建",async ()=> {if(await documents.CanLeave(()=>Choose(window))) documents.Set(ModuleDocument.Create());},()=>true,"Ctrl+N");
        Add("document.open","打开",async ()=> {var location=documents.Location;if(await documents.CanLeave(()=>Choose(window))) documents.Set(ModuleDocument.Open(location));},()=>true,"Ctrl+O");
        Add("document.save","保存",()=>Run(documents.Save),()=>documents.Current!=null,"Ctrl+S",CommandFocus.Global);
        Add("document.close","关闭文档",async ()=> {if(await documents.CanLeave(()=>Choose(window))) documents.Set(null);},()=>documents.Current!=null);
        Add("document.undo","撤销",()=>Run(documents.Undo),()=>documents.CanUndo,"Ctrl+Z");
        Add("document.redo","重做",()=>Run(documents.Redo),()=>documents.CanRedo,"Ctrl+Y");
        Add("document.apply","应用配置草稿",()=>Run(documents.ApplyDrafts),()=>documents.Current!=null);
        void Add(string id,string title,Func<Task> action,Func<bool> enabled,string? shortcut=null,CommandFocus focus=CommandFocus.Author)
            =>commands.Register(scope,new(id,title,action,enabled,shortcut,focus));
    }
    private static Task Run(Action action) {action();return Task.CompletedTask;}
    public static async Task<UnsavedChoice> Choose(Window owner)
    {
        var dialog=new Window {Title="未保存的模块",Width=460,Height=180,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        foreach(var (label,result) in new[] {("保存",UnsavedChoice.Save),("放弃",UnsavedChoice.Discard),("取消",UnsavedChoice.Cancel)})
        {var button=new Button {Content=label};button.Click+=(_,_)=>dialog.Close(result);buttons.Children.Add(button);}
        dialog.Content=new StackPanel {Margin=new Thickness(20),Spacing=16,Children={new TextBlock {Text="文档或配置草稿有未保存修改，是否保存？"},buttons}};
        return await dialog.ShowDialog<UnsavedChoice>(owner);
    }
}

public sealed class ConfigurationPanel : UserControl
{
    public TextBox Source { get; }=new() {AcceptsReturn=true,FontFamily=Avalonia.Media.FontFamily.Parse("Consolas")};
    public ConfigurationPanel(EditorPluginScope scope,string draftName="module.json")
    {
        var documents=scope.Service<DocumentWorkspace>("documents");
        var reset=new Button {Content="放弃此草稿并重新读取"};reset.Click+=(_,_)=>documents.DiscardDraft(draftName);
        var root=new DockPanel();DockPanel.SetDock(reset,Dock.Top);root.Children.Add(reset);root.Children.Add(new ScrollViewer {Content=Source});Content=root;
        var loading=false;
        void Refresh()
        {
            loading=true;
            try {Source.IsEnabled=documents.Current!=null;var text=documents.Current==null?"":documents.Draft(draftName).Text;if(Source.Text!=text) Source.Text=text;}
            finally {loading=false;}
        }
        Source.TextChanged+=(_,_)=> {if(!loading && documents.Current!=null) documents.EditDraft(draftName,Source.Text ?? "");};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
}
