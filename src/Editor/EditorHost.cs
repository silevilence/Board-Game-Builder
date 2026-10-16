using Avalonia.Controls;
using Avalonia.Threading;
using CordiSharp.Plugins.Avalonia;

namespace BoardGame.Editor;

// Startup, root context, composition and startup-failure reporting only.
public sealed class EditorHost
{
    public EditorApplication Application { get; }=new(action=>Dispatcher.UIThread.Invoke(action),async action=>await Dispatcher.UIThread.InvokeAsync(action));
    public Window Window { get; }=new() {Title="Board Game Builder · Editor",Width=1400,Height=900};
    public Task Ready { get; private set; }=Task.CompletedTask;
    private bool closing;
    private bool checkingClose;
    public EditorHost(string? initial=null,bool baseOnly=false)
    {
        Window.Content=new TextBlock {Text="正在加载编辑器…"};
        Window.Opened+=(_,_)=>Ready=Start(initial,baseOnly);
        Window.Closing+=async (_,e)=>
        {
            if(closing) return;
            e.Cancel=true;
            if(checkingClose) return;
            checkingClose=true;
            try {if(await Application.RequestShutdown()) {closing=true;Window.Close();}}
            catch(Exception error) {Application.Service<WorkspaceShell>("workspace").Report("关闭失败："+error.Message);}
            finally {checkingClose=false;}
        };
    }
    private async Task Start(string? initial,bool baseOnly)
    {
        try
        {
            await Application.Root.Plugin(typeof(AvaloniaUIService));
            BuiltinPlugins.Install(Application,Window);
            foreach(var plugin in Application.Catalog.Where(p=>p.Required)) await Application.Enable(plugin.Id);
            Application.Service<DocumentWorkspace>("documents").Set(initial==null?ModuleDocument.Create():ModuleDocument.Open(initial));
            if(!baseOnly) foreach(var plugin in Application.Catalog.Where(p=>!p.Required))
                try {await Application.Enable(plugin.Id);} catch(Exception error) {Application.Service<WorkspaceShell>("workspace").Report(plugin.Id+": "+error.Message);}
        }
        catch(Exception error)
        {
            await Application.DisposeAsync();
            Window.Content=new TextBox {IsReadOnly=true,AcceptsReturn=true,Text="编辑器启动失败\n"+error};
        }
    }
}
