using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;

public sealed class DebugPlayerWindow : Window
{
    public DebugPlayerWindow()
    {
        Title="Player · 编辑器独立调试"; Width=1050; Height=800;
        var table=new InteractiveTable {InputEnabled=false}; var status=new TextBlock {Text="等待编辑器调试握手…"};
        var root=new DockPanel(); DockPanel.SetDock(status,Dock.Top); root.Children.Add(status); root.Children.Add(table); Content=root;
        Opened+=(_,_)=>_ = Task.Run(()=> {
            DebugTransport.Serve((reply,module)=>Dispatcher.UIThread.Post(()=> {
                table.Show(TableScene.From(reply.Frame.View,module.ResourceData));
                status.Text=$"编辑器控制 · 席位 {reply.Frame.View.Seat} · {reply.Frame.View.Phase} · 命令 {reply.Frame.Revision}";
            })); Dispatcher.UIThread.Post(Close);
        });
        Closed+=(_,_)=> { table.Dispose(); Environment.Exit(0); };
    }
}
