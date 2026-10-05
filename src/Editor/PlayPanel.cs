using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed class PlayPanel : UserControl,IDisposable
{
    private readonly Func<ModuleDocument?> document;
    private readonly InteractiveTable table=new();
    private readonly StackPanel actions=new() {Width=260,Spacing=6};
    private readonly TextBlock status=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBox seed=new() {Text="42",Width=100};
    private readonly StackPanel seats=new() {Orientation=Avalonia.Layout.Orientation.Horizontal};
    private readonly Dictionary<string,CheckBox> cpuSeats=new();
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(100)};
    private IDebugSession? session;
    private LoadedModule? module;
    private DebugFrame? frame;
    private bool busy;
    private int generation;
    public PlayPanel(Func<ModuleDocument?> document)
    {
        this.document=document;
        var start=new Button {Content="开始进程内试玩"}; var stop=new Button {Content="停止试玩"};
        var top=new WrapPanel {Children={new TextBlock {Text="种子"},seed,seats,start,stop}};
        var side=new DockPanel {Width=280}; DockPanel.SetDock(status,Dock.Top); side.Children.Add(status); side.Children.Add(new ScrollViewer {Content=actions});
        var root=new DockPanel(); DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(side,Dock.Right); root.Children.Add(top); root.Children.Add(side); root.Children.Add(table); Content=root;
        start.Click+=async (_,_)=>await Start(); stop.Click+=(_,_)=>Stop();
        table.Gesture+=async g=> { if(frame!=null) await Send(new("submit",frame.Revision,PlayerSession.Resolve(frame.Actions,new(g.Kind,g.Entity,g.Target,g.X,g.Y)))); };
        table.RenderingError+=error=> { Stop(); status.Text=error; };
        timer.Tick+=async (_,_)=> { if(frame is {Cpu:true,View.Ended:false,Fault:null} && !busy) await Send(new("cpu",frame.Revision)); };
    }
    public void DocumentChanged()
    {
        Stop(); seats.Children.Clear(); cpuSeats.Clear();
        try
        {
            foreach(var seat in document()?.Snapshot().Initial.Seats ?? [])
            { var check=new CheckBox {Content=seat.Id+" CPU",IsChecked=seat.Controller=="cpu",Margin=new Thickness(6)}; cpuSeats.Add(seat.Id,check); seats.Children.Add(check); }
        }
        catch(Exception e) {status.Text=e.Message;}
    }
    private async Task Start()
    {
        Stop(); var current=generation;
        try
        {
            if(!uint.TryParse(seed.Text,out var number)) throw new InvalidDataException("种子必须是无符号整数");
            module=document()?.Snapshot() ?? throw new InvalidOperationException("请打开模块");
            var controllers=cpuSeats.ToDictionary(s=>s.Key,s=>s.Value.IsChecked==true?"cpu":"human");
            var observer=module.Initial.Seats.FirstOrDefault(s=>controllers.GetValueOrDefault(s.Id)=="human")?.Id ?? module.Initial.Seats[0].Id;
            session=new InProcessDebugSession(module,number,controllers,observer);
            await Send(new("frame")); if(current==generation) timer.Start();
        }
        catch(Exception e) {status.Text=e.Message;}
    }
    private async Task Send(DebugRequest request)
    {
        if(busy || session==null) return;
        busy=true; table.InputEnabled=false; actions.IsEnabled=false; var current=generation;
        try { var reply=await session.SendAsync(request); if(current!=generation) return; frame=reply.Frame; Refresh(reply.Message.Length>0?reply.Message:reply.Result?.Reason ?? ""); }
        catch(Exception e) { if(current==generation) { timer.Stop(); status.Text="试玩故障："+e.Message; } }
        finally { if(current==generation) { busy=false; actions.IsEnabled=true; table.InputEnabled=frame is {Cpu:false,View.Ended:false,Fault:null}; } }
    }
    private void Refresh(string message)
    {
        if(frame==null || module==null) return;
        table.Show(TableScene.From(frame.View,module.ResourceData));
        status.Text=$"试玩 · {frame.View.Seat} | 当前 {frame.View.CurrentSeat}\n{frame.View.Phase} · {frame.View.Turn}\n"+(frame.View.Ended?"结算："+string.Join(",",frame.View.Winners):frame.Fault?.Detail ?? message);
        actions.Children.Clear();
        foreach(var offer in frame.Actions)
        {
            var revision=frame.Revision; var button=new Button {Content=offer.Label}; button.Click+=async (_,_)=>await Send(new("submit",revision,offer.Command)); actions.Children.Add(button);
        }
    }
    public async Task<string> ReloadScripts(LoadedModule candidate)
    {
        if(session==null) return "未开始试玩，新局将使用当前脚本。";
        if(busy) throw new InvalidOperationException("当前命令尚未完成，请在命令边界重试。");
        busy=true;
        try { var reply=await session.SendAsync(new("reload",Module:candidate)); frame=reply.Frame; Refresh(reply.Message); return reply.Message; }
        finally {busy=false;}
    }
    public void Stop() { generation++; timer.Stop(); session?.Dispose(); session=null; frame=null; busy=false; actions.Children.Clear(); table.Show(new([],new Dictionary<string,byte[]>())); status.Text="已停止；作者初始局面保持不变。"; }
    public void Dispose() { Stop(); table.Dispose(); }
}
