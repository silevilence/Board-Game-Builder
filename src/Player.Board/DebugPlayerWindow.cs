using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;

public sealed class DebugPlayerWindow : Window
{
    public DebugPlayerWindow()
    {
        Title="Player · 编辑器独立调试";Width=1050;Height=800;
        InteractiveTable? table=null;BoardGraphics? graphics=null;
        var status=new TextBlock{Text="等待编辑器调试握手…"};var content=new ContentControl();
        var root=new DockPanel();DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);root.Children.Add(content);Content=root;
        var cancellation=new CancellationTokenSource();var pending=Task.CompletedTask;var closing=false;var released=false;
        Opened+=(_,_)=>pending=Run();
        async Task Run()
        {
            await Task.Run(()=>DebugTransport.Serve((reply,module)=>
            {
                Dispatcher.UIThread.Invoke(()=>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var renderer=BoardRenderContract.Require(module.RuntimeIdentity!.Startup);
                    if(table==null)
                    {
                        graphics=new(module.RuntimeIdentity.Startup);
                        table=graphics.Configuration is {} configuration?new(configuration,renderer):new(renderer);
                        table.InputEnabled=false;content.Content=table;
                    }
                    table.Show(TableScene.From(reply.Frame.View,module.Resources));
                    status.Text=$"编辑器控制 · 席位 {reply.Frame.View.Seat} · {reply.Frame.View.Phase} · 命令 {reply.Frame.Revision}";
                });
                for(var i=0;i<300;i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var ready=Dispatcher.UIThread.Invoke(()=>
                    {if(table!.Surface.Error is {} error)throw new InvalidDataException(error);return table.RenderedFrames>0;});
                    if(ready)return;Thread.Sleep(20);
                }
                throw new TimeoutException("Debug board viewport did not render");
            },cancellation.Token));
            if(!closing)Close();
        }
        Closing+=async(_,e)=>
        {
            if(released)return;e.Cancel=true;if(closing)return;closing=true;cancellation.Cancel();
            try{await pending;}finally
            {
                table?.Dispose();content.Content=null;if(table!=null)await table.Shutdown;
                graphics?.Dispose();cancellation.Dispose();released=true;Close();
            }
        };
    }
}
