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
        app.Install(new("author.runtime.board",1,[],["documents","runtime.catalog"],scope=>
        {
            scope.Own(scope.Service<DocumentWorkspace>("documents").Runtimes.Register(new BoardGame.Authoring.BoardAuthorRuntime()));
            scope.Own(scope.Service<BoardGame.Authoring.AuthorRuntimeCatalog>("runtime.catalog").Register("board",BoardGame.Runtime.GameRuntime.Describe));
        }));
        app.Install(new("author.module",1,[],["documents"],scope=>
            scope.Own(scope.Service<DocumentWorkspace>("documents").AuthorFormats.Register(BoardGame.Authoring.GameAuthorFormats.ModuleProvider))));
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
        app.Install(new("runtime.plugins",1,["runtime.plugins"],[],scope=>
            scope.Provide("runtime.plugins",BoardGame.Authoring.AuthorRuntimePlugins.CreateStore()),true));
        app.Install(new("runtime.catalog",1,["runtime.catalog"],["runtime.plugins"],scope=>
            scope.Provide("runtime.catalog",new BoardGame.Authoring.AuthorRuntimeCatalog(scope.Service<BoardGame.Plugins.PluginStore>("runtime.plugins"))),true));
        app.Install(new("base.sessions",1,["sessions"],["documents","runtime.plugins","viewports"],scope=>
        {
            var sessions=new ApplicationSessions(scope.Service<DocumentWorkspace>("documents"),scope.Service<BoardGame.Plugins.PluginStore>("runtime.plugins"),scope.Service<EditorViewports>("viewports").RequireCurrent);
            scope.OwnAsync(sessions);scope.Provide("sessions",sessions);
        },true));
        app.Install(new("base.viewports",1,["viewports"],["runtime.catalog","documents","commands"],scope=>
        {
            var views=new EditorViewports(scope.Service<DocumentWorkspace>("documents"),new(BoardGame.Rendering.WindowsDxBackend.Plugin,
                Path.Combine(AppContext.BaseDirectory,"backends","desktopgl")));
            scope.Provide("viewports",views);scope.OwnAsync(views);
            var commands=scope.Service<EditorCommands>("commands");
            commands.Register(scope,new("graphics.rebuild","应用后端并重建视口",views.Apply,()=>!views.Busy && scope.Service<DocumentWorkspace>("documents").Current!=null));
            commands.Register(scope,new("graphics.cancel","取消图形重建",views.CancelAndWait,()=>views.Busy));
            views.Changed+=commands.Refresh;scope.Own(new Cleanup(()=>views.Changed-=commands.Refresh));
            scope.Own(app.BeforeShutdown(async()=>{await views.CancelAndWait();return true;}));
            scope.Own(scope.Service<BoardGame.Authoring.AuthorRuntimeCatalog>("runtime.catalog").Register("graphics",_=>BoardGame.Rendering.GraphicsBackendContract.Descriptions));
        },true));
        app.Install(new("play.board.sessions",1,["board.sessions"],["documents","viewports"],scope=>
        {
            var sessions=new BoardSessionController(scope.Service<DocumentWorkspace>("documents"),scope.Service<EditorViewports>("viewports").RequireCurrent);scope.Own(sessions);scope.Provide("board.sessions",sessions);
        }));
        app.Install(new("board.viewports",1,["board.viewports"],["viewports"],scope=>scope.Provide("board.viewports",new BoardViewports(scope.Service<EditorViewports>("viewports")))));
        app.Install(new("runtime.motion",1,[],["sessions","documents","runtime.plugins","runtime.catalog"],scope=>
        {
            var catalog=new BoardGame.AppRuntime.ApplicationCatalog(Sample.Motion.MotionPlugins.Catalog());
            scope.Own(scope.Service<ApplicationSessions>("sessions").Register("motion",catalog));
            scope.Own(scope.Service<BoardGame.Authoring.AuthorRuntimeCatalog>("runtime.catalog").Register("motion",_=>catalog.Content.Plugins));
            scope.Own(scope.Service<DocumentWorkspace>("documents").Runtimes.Register(new BoardGame.Authoring.ApplicationAuthorRuntime("motion",catalog,scope.Service<BoardGame.Plugins.PluginStore>("runtime.plugins"))));
        }));
        app.Install(new("runtime.external",1,[],["sessions","documents","runtime.plugins"],scope=>
        {
            var catalog=new BoardGame.AppRuntime.ApplicationCatalog([BoardGame.Resources.AssetStaticFactories.Resources(),BoardGame.Resources.AssetStaticFactories.Images(),
                BoardGame.Resources.AssetStaticFactories.TexturePacker(),BoardGame.Resources.AssetStaticFactories.Font()]);
            var store=scope.Service<BoardGame.Plugins.PluginStore>("runtime.plugins");
            scope.Own(scope.Service<ApplicationSessions>("sessions").Register("external",catalog,true));
            scope.Own(scope.Service<DocumentWorkspace>("documents").Runtimes.Register(new BoardGame.Authoring.ApplicationAuthorRuntime("external",catalog,store,true)));
        }));
        app.Install(new("play.application",1,[],["documents","sessions","viewports","panels"],scope=>
        {
            var sessions=scope.Service<ApplicationSessions>("sessions");
            scope.Service<PanelContributions>("panels").Register(scope,"play.application","应用试玩",()=>new FormatPanel(scope,s=>new ApplicationPlayPanel(s),()=>sessions.Supported,()=>"当前内容没有通用应用试玩提供者；桌游请使用桌游试玩面板。"));
        }));
        app.Install(new("base.assemblies",1,["assemblies"],[],_=>{},true)
        {
            InitializeAsync=async scope=>
            {
                await app.Root.Plugin(typeof(CordiSharp.Loading.AssemblyLoaderService));
                scope.Provide("assemblies",new EditorAssemblies(app,app.Root.Get<CordiSharp.Loading.AssemblyLoaderService>("loader")!));
            }
        });
        app.Install(new("author.layout",1,[],["documents","board.viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.layout","布局视口",()=>FormatPanel.Board(scope,s=>new LayoutPanel(s)))));
        app.Install(new("play.controls",1,[],["documents","board.sessions","board.viewports","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"play.controls","桌游试玩与调试",()=>new FormatPanel(scope,s=>new PlayPanel(s),()=>BoardSessionController.Supports(scope.Document)))));
        app.Install(new("author.scripts",1,[],["documents","board.sessions","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.scripts","Lua 脚本",()=>new FormatPanel(scope,s=>new ScriptPanel(s),()=>BoardSessionController.Supports(scope.Document)))));
        app.Install(FlowEditorPlugin.Definition);
        app.Install(AnimationEditorPlugin.Definition);
        app.Install(new("author.configuration",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.configuration","模块配置",()=>new FormatPanel(scope,s=>new ConfigurationPanel(s)))));
        app.Install(new("author.startup",1,["runtime.configuration"],["documents","panels","runtime.catalog","sessions","board.sessions","viewports","commands"],scope=>
        {
            var documents=scope.Service<DocumentWorkspace>("documents");
            var configuration=new RuntimeConfiguration(documents,scope.Service<BoardGame.Authoring.AuthorRuntimeCatalog>("runtime.catalog"),scope.Service<ApplicationSessions>("sessions"),scope.Service<BoardSessionController>("board.sessions"));
            scope.Provide("runtime.configuration",configuration);scope.OwnAsync(configuration);
            documents.Replaced+=configuration.DocumentReplaced;scope.Own(new Cleanup(()=>documents.Replaced-=configuration.DocumentReplaced));
            scope.Service<PanelContributions>("panels").Register(scope,"author.startup","运行插件配置",()=>new RuntimeConfigurationPanel(scope));
        }));
        app.Install(new("author.files",1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.files","游戏文件",()=>new GameFilePanel(scope))));
        app.Install(new("author.runtime.management",1,["runtime.management"],["runtime.plugins","runtime.catalog","runtime.configuration","documents","panels","commands"],scope=>
        {
            var management=new RuntimePluginManagement(scope.Service<BoardGame.Plugins.PluginStore>("runtime.plugins"),scope.Service<BoardGame.Authoring.AuthorRuntimeCatalog>("runtime.catalog"));
            scope.Provide("runtime.management",management);scope.OwnAsync(management);
            var commands=scope.Service<EditorCommands>("commands");var documents=scope.Service<DocumentWorkspace>("documents");var configuration=scope.Service<RuntimeConfiguration>("runtime.configuration");
            commands.Register(scope,new("runtime.plugins.import","导入运行插件",()=>management.Import(false),()=>!management.Busy && management.PackagePath.Length>0));
            commands.Register(scope,new("runtime.plugins.replace","替换运行插件版本",()=>management.Import(true),()=>!management.Busy && management.PackagePath.Length>0));
            commands.Register(scope,new("runtime.plugins.remove","移除运行插件版本",()=>management.Remove(management.Selection!),()=>!management.Busy && management.Selection!=null));
            commands.Register(scope,new("runtime.plugins.enable","启用所选运行插件",()=>
            {
                configuration.Select(configuration.Available().Single(p=>p.Manifest?.Fingerprint==management.Selection!.Fingerprint));return Task.CompletedTask;
            },()=>!management.Busy && management.Selection!=null && documents.Current!=null));
            commands.Register(scope,new("runtime.plugins.cancel","取消插件安装操作",management.CancelAndWait,()=>management.Busy));
            management.Changed+=commands.Refresh;scope.Own(new Cleanup(()=>management.Changed-=commands.Refresh));
            scope.Own(app.BeforeShutdown(async()=>{await management.CancelAndWait();return true;}));
            scope.Service<PanelContributions>("panels").Register(scope,"author.runtime.management","运行插件管理",()=>new RuntimePluginManagementPanel(scope));
        }));
        Panel("author.resources","资源树",scope=>new ResourcePanel(scope));
        Panel("author.yaml","模块 YAML",scope=>new ConfigurationPanel(scope,yaml:true));
        Panel("author.prototypes","原型与实例",scope=>new AuthorPanel(scope));
        Panel("author.media","媒介组件",scope=>new MediaInspector(scope));
        Panel("author.turns","席位与阶段",scope=>new TurnPanel(scope));
        Panel("author.extensions","WASM 媒介",scope=>new ExtensionPanel(scope));
        app.Install(new("author.images",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.images","图片与卡图导入",()=>FormatPanel.Board(scope,s=>new ImageImportPanel(s)))));
        app.Install(new("author.tts",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.tts","TTS 导入",()=>FormatPanel.Board(scope,s=>new TtsImportPanel(s)))));
        app.Install(new("author.print",1,[],["documents","commands","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,"author.print","打印拼版",()=>FormatPanel.Board(scope,s=>new PrintPanel(s)))));
        app.Install(new("author.export",1,["exports","author.exports"],["documents","contributions","panels","workspace","commands"],scope=>
        {
            var providers=new BoardGame.Authoring.AuthorExports();scope.Provide("author.exports",providers);
            var workflow=new ExportWorkflow(scope.Service<DocumentWorkspace>("documents"),scope.Service<EditorContributions>("contributions"),providers);scope.OwnAsync(workflow);scope.Provide("exports",workflow);
            scope.Guard(()=>workflow.Running?"导出正在执行，请等待完成":null);
            scope.Own(app.BeforeShutdown(async()=>{await workflow.CancelAndWait();return true;}));
            var commands=scope.Service<EditorCommands>("commands");
            commands.Register(scope,new("author.export.check","发布依赖预检",async()=>{await workflow.Run(false);},()=>!workflow.Running && workflow.Providers.Items.Any(p=>p.Id==workflow.Selected)));
            commands.Register(scope,new("author.export.run","导出当前草稿",async()=>{await workflow.Run(true);},()=>!workflow.Running && workflow.Providers.Items.Any(p=>p.Id==workflow.Selected)));
            commands.Register(scope,new("author.export.cancel","取消发布并等待退出",workflow.CancelAndWait,()=>workflow.Running));
            workflow.Changed+=commands.Refresh;scope.Own(new Cleanup(()=>workflow.Changed-=commands.Refresh));
            scope.Service<PanelContributions>("panels").Register(scope,"author.export","独立导出",()=>new ExportPanel(scope));
        }));
        ExportProvider(new BoardGame.Authoring.PlayerExporter(),new Dictionary<string,string>{["player"]=EditorPaths.ReleasePlayer,["target"]=EditorPaths.InWorkspace("artifacts/export-game")});
        ExportProvider(new BoardGame.Authoring.PackageExporter(),new Dictionary<string,string>{["target"]=EditorPaths.InWorkspace("artifacts/game.bgm")});
        ExportProvider(new BoardGame.Authoring.NativeExporter(),new Dictionary<string,string>{["target"]=EditorPaths.InWorkspace("artifacts/native-game"),["sdk"]=Path.Combine(AppContext.BaseDirectory,"native-sdk"),["host"]="application.desktop",["platform"]="win-x64"});
        void ExportProvider(BoardGame.Authoring.IAuthorExporter provider,IReadOnlyDictionary<string,string> defaults)=>app.Install(new(provider.Info.Plugin,1,[],["exports","author.exports","documents","commands"],scope=>
        {
            var workflow=scope.Service<ExportWorkflow>("exports");var documents=scope.Service<DocumentWorkspace>("documents");
            scope.OwnAsync(workflow.RegisterProvider(provider));
            scope.Guard(()=>workflow.ActiveProvider==provider.Info.Id?"导出正在执行，请等待完成":null);
            void Defaults()=>workflow.Defaults(provider.Info.Id,defaults);
            documents.Replaced+=Defaults;scope.Own(new Cleanup(()=>documents.Replaced-=Defaults));Defaults();
            scope.Service<EditorCommands>("commands").Register(scope,new(provider.Info.Operation.Id,provider.Info.Title,async()=>
            {workflow.Selected=provider.Info.Id;await workflow.Run(true);},()=>documents.Current!=null && !workflow.Running));
        }));
        void Panel(string id,string title,Func<EditorPluginScope,Control> factory)=>app.Install(new(id,1,[],["documents","panels"],scope=>
            scope.Service<PanelContributions>("panels").Register(scope,id,title,()=>id=="author.resources"?factory(scope):id=="author.yaml"?new FormatPanel(scope,factory):FormatPanel.Board(scope,factory))));
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
