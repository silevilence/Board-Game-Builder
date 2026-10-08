using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed class ScriptPanel : UserControl
{
    public ScriptPanel(EditorPluginScope context)
    {
        var sessions=context.Service<SessionController>("sessions");
        var cancellation=context.Cancellation;
        var rules=new TextBox {AcceptsReturn=true,FontFamily=FontFamily.Parse("Consolas")};
        var cpu=new TextBox {AcceptsReturn=true,FontFamily=FontFamily.Parse("Consolas")};
        var diagnostics=new TextBox {IsReadOnly=true,AcceptsReturn=true,MinHeight=80,TextWrapping=TextWrapping.Wrap};
        var apply=new Button {Content="应用规则与 CPU 脚本"}; var hot=new Button {Content="在下一命令边界热重载"};
        var scriptTabs=new TabControl(); var ruleTab=new TabItem {Header="规则",Content=new ScrollViewer {Content=rules}}; var cpuTab=new TabItem {Header="CPU",Content=new ScrollViewer {Content=cpu}};
        scriptTabs.Items.Add(ruleTab); scriptTabs.Items.Add(cpuTab);
        var top=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Children={apply,hot}};
        var root=new DockPanel {Margin=new Thickness(8)}; DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(diagnostics,Dock.Bottom); root.Children.Add(top); root.Children.Add(diagnostics); root.Children.Add(scriptTabs); Content=root;
        ModuleDocument? loaded=null; string rulePath="",cpuPath="";
        void Refresh()
        {
            loaded=context.Document;
            rulePath=loaded?.ScriptPath("lua.rules") ?? ""; cpuPath=loaded?.ScriptPath("lua.cpu") ?? "";
            rules.IsEnabled=rulePath.Length>0;cpu.IsEnabled=cpuPath.Length>0;
            var ruleText=rulePath.Length==0?"未启用 Lua 规则":loaded?.Text(rulePath); var cpuText=cpuPath.Length==0?"未启用 Lua CPU":loaded?.Text(cpuPath);
            if(rules.Text!=ruleText) rules.Text=ruleText; if(cpu.Text!=cpuText) cpu.Text=cpuText;
            ruleTab.Header=rulePath+" · actions / decide"; cpuTab.Header=cpuPath+" · decide";
        }
        // Editing immediately joins document undo/save/dirty handling; loading is guarded.
        var loading=false;
        void Sync() { if(loading) return; loading=true; try { Refresh(); } finally {loading=false;} }
        rules.PropertyChanged+=(_,e)=> { if(e.Property==TextBox.TextProperty && !loading && loaded!=null && rulePath.Length>0) loaded.EditText(rulePath,rules.Text ?? ""); };
        cpu.PropertyChanged+=(_,e)=> { if(e.Property==TextBox.TextProperty && !loading && loaded!=null && cpuPath.Length>0) loaded.EditText(cpuPath,cpu.Text ?? ""); };
        apply.Click+=(_,_)=>diagnostics.Text="脚本修改已进入文档；保存写入模块。配置变化需要重开对局。";
        hot.Click+=async (_,_)=> {
            hot.IsEnabled=false;
            try { if(context.Document==null) return; var result=await sessions.ReloadScripts(context.Document.Snapshot());if(!cancellation.IsCancellationRequested) diagnostics.Text=result; }
            catch(Exception e) { if(!cancellation.IsCancellationRequested) diagnostics.Text="重载失败，当前会话继续使用旧版：\n"+e.Message; }
            finally {if(!cancellation.IsCancellationRequested) hot.IsEnabled=true;}
        };
        context.OnDocumentChanged(Sync); Sync();
    }
}
