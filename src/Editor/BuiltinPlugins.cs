using Avalonia.Controls;
using CordiSharp.Plugins.Avalonia;

namespace BoardGame.Editor;

public static class BuiltinPlugins
{
    public static void Install(EditorApplication app,Window window)
    {
        app.Install(new("base.workspace",1,["workspace","panels"],[],scope=>
        {
            var shell=new WorkspaceShell();window.Content=shell;
            scope.Provide("workspace",shell);
            scope.Provide("panels",new PanelContributions(app.Root.Get<AvaloniaUIService>("avalonia")!));
            scope.Own(new Cleanup(()=>window.Content=null));
        },true));
        app.Install(new("base.documents",1,["documents"],["workspace"],scope=>
        {
            var documents=new DocumentWorkspace();scope.Own(documents);scope.Provide("documents",documents);
            var shell=scope.Service<WorkspaceShell>("workspace");
            var path=new TextBox {Width=400,PlaceholderText="模块目录"};shell.Toolbar.Children.Add(path);
            scope.Own(new Cleanup(()=>shell.Toolbar.Children.Remove(path)));
            Add("新建",()=>documents.Set(ModuleDocument.Create()));
            Add("打开",()=>documents.Set(ModuleDocument.Open(path.Text ?? "")));
            Add("保存",()=>documents.Current?.Save(path.Text));
            void Add(string title,Action action)
            {
                var button=new Button {Content=title};button.Click+=(_,_)=> {try {action();shell.Report("完成");} catch(Exception e) {shell.Report(e.Message);}};
                shell.Toolbar.Children.Add(button);scope.Own(new Cleanup(()=>shell.Toolbar.Children.Remove(button)));
            }
        },true));
        app.Install(new("base.plugins",1,[],["panels","workspace"],scope=>
        {
            var list=new StackPanel {Spacing=8};
            void Refresh()
            {
                list.Children.Clear();
                foreach(var state in app.States)
                {
                    var row=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10};
                    row.Children.Add(new TextBlock {Text=$"{state.Id} · {state.Status} · {state.Reason} {app.BlockedReason(state.Id)}",Width=650});
                    foreach(var (title,action) in new (string,Func<Task>)[] {("启用",()=>app.Enable(state.Id)),("停用",()=>app.Disable(state.Id)),("重载",()=>app.Reload(state.Id))})
                    {
                        var button=new Button {Content=title};button.Click+=async (_,_)=> {try {await action();} catch(Exception e) {scope.Service<WorkspaceShell>("workspace").Report(e.Message);}};row.Children.Add(button);
                    }
                    list.Children.Add(row);
                }
            }
            app.Changed+=Refresh;scope.Own(new Cleanup(()=>app.Changed-=Refresh));Refresh();
            scope.Service<PanelContributions>("panels").Register(scope,"base.plugins","插件管理",()=>new ScrollViewer {Content=list});
        },true));
        app.Install(new("base.settings",1,[],["panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"base.settings","设置",()=>new TextBlock {Text="编辑器：Avalonia 12.1.1 / CordiSharp 0.2.0\n视口：MonoGame WindowsDX\n工作区：功能标签页；原生视口区域遵守 airspace 限制。"}),true));
    }
}
