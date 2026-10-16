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
            var commands=new EditorCommands(shortcut=>Avalonia.Input.KeyGesture.Parse(shortcut).ToString());scope.Provide("commands",commands);
            scope.Provide("contributions",new EditorContributions());
            scope.Own(new CommandSurface(window,scope.Service<WorkspaceShell>("workspace"),commands));
        },true));
        app.Install(new("base.documents",1,["documents"],["workspace","commands"],scope=>DocumentPlugin.Activate(app,window,scope),true));
        app.Install(new("author.workspace",1,["author.workspace"],["documents","panels","commands"],scope=>
        {
            scope.Provide("author.workspace",new AuthorWorkspaceService(scope.Service<DocumentWorkspace>("documents"),
                async action=>await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action)));
            scope.Service<PanelContributions>("panels").Register(scope,"author.workspace","外部编辑与连接",()=>new AuthorWorkspacePanel(scope));
        }));
        app.Install(new("author.connection",1,[],["author.workspace","documents"],scope=>
        {
            var service=scope.Service<AuthorWorkspaceService>("author.workspace");
            var documents=scope.Service<DocumentWorkspace>("documents");
            var server=new BoardGame.Authoring.WorkspaceConnectionServer(service.Handle);
            scope.Context.Effect(()=>server);
            void Publish()=>server.Publish(documents.WorkspaceId,documents.Generation,documents.Current?.DirectoryPath);
            documents.Replaced+=Publish;scope.Own(new Cleanup(()=>documents.Replaced-=Publish));Publish();
        }));
        app.Install(new("base.sessions",1,["sessions"],["documents"],scope=>
        {
            var sessions=new SessionController(scope.Service<DocumentWorkspace>("documents"));scope.Own(sessions);scope.Provide("sessions",sessions);
        },true));
        app.Install(new("base.viewports",1,["viewports"],[],scope=>scope.Provide("viewports",new EditorViewports()),true));
        app.Install(new("base.assemblies",1,["assemblies"],[],_=>{},true)
        {
            InitializeAsync=async scope=>
            {
                await app.Root.Plugin(typeof(CordiSharp.Loading.AssemblyLoaderService));
                scope.Provide("assemblies",new EditorAssemblies(app,app.Root.Get<CordiSharp.Loading.AssemblyLoaderService>("loader")!));
            }
        });
        app.Install(new("author.layout",1,[],["documents","viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.layout","布局视口",()=>new FormatPanel(scope,s=>new LayoutPanel(s)))));
        app.Install(new("play.controls",1,[],["sessions","viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"play.controls","试玩与调试",()=>new PlayPanel(scope))));
        app.Install(new("author.scripts",1,[],["documents","sessions","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.scripts","Lua 脚本",()=>new ScriptPanel(scope))));
        app.Install(FlowEditorPlugin.Definition);
        app.Install(new("author.configuration",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.configuration","模块配置",()=>new FormatPanel(scope,s=>new ConfigurationPanel(s)))));
        app.Install(new("author.startup",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.startup","运行插件配置",()=>new ConfigurationPanel(scope,BoardGame.Runtime.GameRuntime.Entry))));
        app.Install(new("author.files",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.files","游戏文件",()=>new GameFilePanel(scope))));
        Panel("author.resources","资源树",scope=>new ResourcePanel(scope));
        Panel("author.yaml","模块 YAML",scope=>new ConfigurationPanel(scope,yaml:true));
        Panel("author.prototypes","原型与实例",scope=>new AuthorPanel(scope));
        Panel("author.media","媒介组件",scope=>new MediaInspector(scope));
        Panel("author.turns","席位与阶段",scope=>new TurnPanel(scope));
        Panel("author.extensions","WASM 媒介",scope=>new ExtensionPanel(scope));
        app.Install(new("author.images",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.images","图片与卡图导入",()=>new FormatPanel(scope,s=>new ImageImportPanel(s)))));
        app.Install(new("author.tts",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.tts","TTS 导入",()=>new FormatPanel(scope,s=>new TtsImportPanel(s)))));
        app.Install(new("author.print",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.print","打印拼版",()=>new FormatPanel(scope,s=>new PrintPanel(s)))));
        app.Install(new("author.export",1,["exports"],["documents","contributions","panels","workspace"],scope=>
        {
            var workflow=new ExportWorkflow(scope.Service<DocumentWorkspace>("documents"),scope.Service<EditorContributions>("contributions"));scope.Provide("exports",workflow);
            scope.Guard(()=>workflow.Running?"导出正在执行，请等待完成":null);
            scope.Own(app.BeforeShutdown(()=>
            {
                if(workflow.Running) scope.Service<WorkspaceShell>("workspace").Report("导出正在执行，请等待完成后退出。");
                return Task.FromResult(!workflow.Running);
            }));
            scope.Service<PanelContributions>("panels").Register(scope,"author.export","独立导出",()=>new ExportPanel(scope));
        }));
        void Panel(string id,string title,Func<EditorPluginScope,Control> factory)=>app.Install(new(id,1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,id,title,()=>id=="author.resources"?factory(scope):new FormatPanel(scope,factory))));
        app.Install(new("base.plugins",1,[],["panels","workspace","assemblies"],scope=>
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
            var path=new TextBox {PlaceholderText="预编译扩展 DLL 路径（旁边需有 .plugin.json）",Width=550};
            var identity=new TextBox {PlaceholderText="要卸载的插件 ID",Width=180};
            var load=new Button {Content="加载程序集"};var unload=new Button {Content="卸载程序集"};
            load.Click+=async (_,_)=> {try {await scope.Service<EditorAssemblies>("assemblies").Load(path.Text ?? "");} catch(Exception e) {scope.Service<WorkspaceShell>("workspace").Report(e.Message);}};
            unload.Click+=async (_,_)=> {try {await scope.Service<EditorAssemblies>("assemblies").Unload(identity.Text ?? "");scope.Service<WorkspaceShell>("workspace").Report("程序集已回收");} catch(Exception e) {scope.Service<WorkspaceShell>("workspace").Report(e.Message);}};
            var panel=new DockPanel();var top=new WrapPanel {Children={path,load,identity,unload}};DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);panel.Children.Add(new ScrollViewer {Content=list});
            scope.Service<PanelContributions>("panels").Register(scope,"base.plugins","插件管理",()=>panel);
        },true));
        app.Install(new("base.settings",1,[],["panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"base.settings","设置",()=>new TextBlock {Text="编辑器：Avalonia 12.1.1 / CordiSharp 0.2.0\n视口：MonoGame WindowsDX\n工作区：功能标签页；原生视口区域遵守 airspace 限制。"}),true));
    }
}
