using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Player;

internal sealed class OnlineWindow : Window
{
    private readonly LoadedModule module;
    private readonly OnlinePlayerSession session;
    private readonly InteractiveTable table=new();
    private readonly StackPanel actions=new() {Spacing=8,Margin=new Thickness(12)};
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12),FontSize=16};
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(350)};
    private bool busy,closed;
    private TaskCompletionSource? operation;
    private string? actionMessage,renderFailure;
    private long revision=-1;
    internal OnlineFrame Frame=>session.Frame!;
    internal int RenderedFrames=>table.RenderedFrames;
    internal string? VerificationError {get;private set;}
    internal Task VerifyRefresh()=>Run(()=>session.RefreshAsync(),true);
    internal Task VerifyRecover()=>Run(async ()=>
    {
        await session.DisconnectAsync();
        await Task.Delay(350);
        await session.ConnectAsync();
    });
    public OnlineWindow(LoadedModule module,OnlinePlayerSession session)
    {
        this.module=module;this.session=session;
        Title=PlayerPresentation.Read(module).Title+" · 联机 · "+session.Frame!.View.Seat;Width=1300;Height=850;MinWidth=800;MinHeight=600;
        var controls=new WrapPanel {Margin=new Thickness(8)};
        void Button(string title,Func<Task> action) {var b=new Button {Content=title,Margin=new Thickness(4)};b.Click+=async (_,_)=>await Run(action);controls.Children.Add(b);}
        Button("适应窗口",()=> {table.Fit();return Task.CompletedTask;});
        Button("暂时断线",()=>session.DisconnectAsync());
        Button("恢复连接",()=>session.ConnectAsync());
        Button("查看对局记录",async ()=> {var record=await session.RecordingAsync();if(!closed)new OnlineReplayWindow(module,record).Show(this);});
        Button("保存本人记录",async ()=>
        {
            var record=await session.RecordingAsync();
            var file=await StorageProvider.SaveFilePickerAsync(new() {Title="保存身份投影记录",SuggestedFileName="online-recording.json"});
            if(file!=null) {await using var stream=await file.OpenWriteAsync();using var writer=new StreamWriter(stream);await writer.WriteAsync(Wire.Json(record));}
        });
        Button("主动离开并结束对局",async ()=> {await session.DisconnectAsync(true);Close();});
        var root=new DockPanel();DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);
        var sidebar=new ScrollViewer {Content=actions,Width=260};DockPanel.SetDock(sidebar,Dock.Right);root.Children.Add(sidebar);root.Children.Add(table);Content=root;
        table.Gesture+=g=>_=Act(new(g.Kind,g.Entity,g.Target,g.X,g.Y),revision);
        table.RenderingError+=error=> {timer.Stop();renderFailure="渲染失败："+error;Refresh();};
        timer.Tick+=async (_,_)=> {if(session.Connected) await Run(()=>session.RefreshAsync(),true);};
        Opened+=(_,_)=> {Refresh();timer.Start();};
        Closed+=(_,_)=> {closed=true;timer.Stop();table.Dispose();session.Dispose();};
    }
    internal Task Act(InteractionIntent intent,long expected)=>Run(async ()=>
    {
        var receipt=await session.SubmitAsync(expected,intent);
        actionMessage=receipt.Result.Accepted?"操作已提交":"操作被拒绝："+receipt.Result.Reason;
    });
    private void Refresh()
    {
        if(closed)return;
        var frame=session.Frame;
        table.InputEnabled=renderFailure==null && session.Connected && !busy && frame is {Started:true,Closed:false,Fault:null} && !frame.View.Ended && frame.Actions.Count>0;
        if(frame==null)return;
        if(frame.Revision!=revision) {revision=frame.Revision;table.Show(TableScene.From(session.Projection.View(),module.Resources));}
        var v=frame.View;
        status.Text=$"身份 {v.Seat} · 当前 {v.CurrentSeat} · {v.Seats.Count} 人 · 阶段 {v.Phase} · 行动 {v.Turn}\n"+
            (!session.Connected?"连接已中断，请点击恢复连接。":frame.Closed?"服务器会话已关闭。":frame.Fault!=null?"会话故障："+frame.Fault:
             !frame.Started?"等待真人席位加入…":v.Ended?(v.Winners.Count==0?"本局平局。":"胜者："+string.Join("、",v.Winners)):"等待当前席位行动。")+
            "\n"+string.Join("  ",v.Seats.Select(s=>$"{s.Id}: {frame.Controllers?.GetValueOrDefault(s.Id) ?? s.Controller}"));
        if(renderFailure!=null) status.Text+="\n"+renderFailure;
        else if(actionMessage!=null) status.Text+="\n"+actionMessage;
        actions.Children.Clear();
        foreach(var offer in frame.Actions)
        {
            var expected=frame.Revision;
            var button=new Button {Content=new TextBlock {Text=offer.Label,TextWrapping=TextWrapping.Wrap},HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=table.InputEnabled};
            button.Click+=async (_,_)=>await Act(new("action",Action:offer.Id),expected);actions.Children.Add(button);
        }
    }
    private async Task Run(Func<Task> action,bool poll=false)
    {
        if(closed)return;
        if(busy)
        {
            if(poll)return;
            if(operation!=null)await operation.Task;
            if(closed || busy)return;
        }
        busy=true;var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);operation=completion;
        if(!poll) {table.InputEnabled=false;actions.IsEnabled=false;}string? error=null;
        try {await action();}catch(Exception e) {error=e.Message;VerificationError??=e.ToString();}
        finally {busy=false;if(!closed) {Refresh();actions.IsEnabled=true;if(error!=null)status.Text+="\n"+error;}completion.TrySetResult();}
    }
}

internal sealed class OnlineReplayWindow : Window
{
    public OnlineReplayWindow(LoadedModule module,OnlineRecording record)
    {
        if(record.Identity!=OnlineIdentity.For(module))throw new OnlineException("version-mismatch");
        _=record.Replay();
        Title="身份记录 · "+record.Seat;Width=1150;Height=800;
        var table=new InteractiveTable {InputEnabled=false};
        var slider=new Slider {Minimum=0,Maximum=record.Frames.Count-1,TickFrequency=1,IsSnapToTickEnabled=true};
        var label=new TextBlock {Margin=new Thickness(10)};
        void Show()
        {
            var frame=record.Frames[(int)Math.Round(slider.Value)];
            table.Show(TableScene.From(frame.View,module.Resources));
            label.Text=$"记录 {frame.Revision}/{record.Frames.Count-1} · {frame.View.CurrentSeat} · {frame.View.Phase}";
        }
        slider.ValueChanged+=(_,_)=>Show();
        var root=new DockPanel();DockPanel.SetDock(slider,Dock.Bottom);root.Children.Add(slider);DockPanel.SetDock(label,Dock.Top);root.Children.Add(label);root.Children.Add(table);Content=root;
        Opened+=(_,_)=>Show();Closed+=(_,_)=>table.Dispose();
    }
}
