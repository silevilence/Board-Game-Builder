using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed class ScriptPanel : UserControl
{
    public ScriptPanel(PluginContext context,Func<LoadedModule,Task<string>> reload)
    {
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
            rulePath=loaded?.Configuration["rules"]?.GetValue<string>() ?? ""; cpuPath=loaded?.Configuration["cpu"]?.GetValue<string>() ?? "";
            var ruleText=loaded?.Text(rulePath); var cpuText=loaded?.Text(cpuPath);
            if(rules.Text!=ruleText) rules.Text=ruleText; if(cpu.Text!=cpuText) cpu.Text=cpuText;
            ruleTab.Header=rulePath+" · actions / decide"; cpuTab.Header=cpuPath+" · decide";
        }
        // Editing immediately joins document undo/save/dirty handling; loading is guarded.
        var loading=false;
        void Sync() { if(loading) return; loading=true; try { Refresh(); } finally {loading=false;} }
        rules.TextChanged+=(_,_)=> { if(!loading && loaded!=null) loaded.EditText(rulePath,rules.Text ?? ""); };
        cpu.TextChanged+=(_,_)=> { if(!loading && loaded!=null) loaded.EditText(cpuPath,cpu.Text ?? ""); };
        apply.Click+=(_,_)=>diagnostics.Text="脚本修改已进入文档；保存写入模块。配置变化需要重开对局。";
        hot.Click+=async (_,_)=> {
            hot.IsEnabled=false;
            try { if(context.Document==null) return; diagnostics.Text=await reload(context.Document.Snapshot()); }
            catch(Exception e) { diagnostics.Text="重载失败，当前会话继续使用旧版：\n"+e.Message; }
            finally {hot.IsEnabled=true;}
        };
        context.OnDocumentChanged(Sync); Sync();
    }
}
