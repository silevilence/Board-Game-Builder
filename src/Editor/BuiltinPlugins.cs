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
        app.Install(new("base.commands",1,["commands","contributions"],["workspace"],scope=>
        {
            var commands=new EditorCommands();scope.Provide("commands",commands);
            scope.Provide("contributions",new EditorContributions());
            scope.Own(new CommandSurface(window,scope.Service<WorkspaceShell>("workspace"),commands));
        },true));
        app.Install(new("base.documents",1,["documents"],["workspace","commands"],scope=>DocumentPlugin.Activate(app,window,scope),true));
        app.Install(new("base.sessions",1,["sessions"],["documents"],scope=>
        {
            var sessions=new SessionController(scope.Service<DocumentWorkspace>("documents"));scope.Own(sessions);scope.Provide("sessions",sessions);
        },true));
        app.Install(new("base.viewports",1,["viewports"],[],scope=>scope.Provide("viewports",new EditorViewports()),true));
        app.Install(new("author.layout",1,[],["documents","viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.layout","布局视口",()=>new LayoutPanel(scope))));
        app.Install(new("play.controls",1,[],["sessions","viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"play.controls","试玩与调试",()=>new PlayPanel(scope))));
        app.Install(new("author.scripts",1,[],["documents","sessions","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.scripts","Lua 脚本",()=>new ScriptPanel(scope))));
        app.Install(new("author.configuration",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.configuration","模块配置",()=>new ConfigurationPanel(scope))));
        Panel("author.resources","资源树",scope=>new ResourcePanel(scope));
        Panel("author.yaml","模块 YAML",scope=>new ConfigurationPanel(scope,"module.yaml"));
        Panel("author.prototypes","原型与实例",scope=>new AuthorPanel(scope));
        Panel("author.media","媒介组件",scope=>new MediaInspector(scope));
        Panel("author.turns","席位与阶段",scope=>new TurnPanel(scope));
        void Panel(string id,string title,Func<EditorPluginScope,Control> factory)=>app.Install(new(id,1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,id,title,()=>factory(scope))));
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
