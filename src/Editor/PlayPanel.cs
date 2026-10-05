using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed partial class PlayPanel : UserControl,IDisposable
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
    private bool pendingPause;
    private int generation;
    private readonly CheckBox external=new() {Content="独立 Player"};
    private readonly TextBox executable=new() {Text=EditorPaths.InWorkspace("src/Player/bin/Release/net10.0-windows/Player.exe"),Width=260,Watermark="调试 Player.exe 路径"};
    private readonly TextBox timeline=new() {IsReadOnly=true,AcceptsReturn=true,Height=170};
    private readonly TextBox logPath=new() {Text=EditorPaths.InWorkspace("artifacts/debug-session.json"),Width=260};
    private readonly CheckBox authorityLog=new() {Content="显示作者命令与 CPU 诊断（含私有数据）",IsChecked=false};
    private DebugReply? lastReply;
    private readonly ComboBox observer=new() {Width=100};
    private readonly CheckBox reveal=new() {Content="作者全信息调试（包含隐藏真值）"};
    public PlayPanel(Func<ModuleDocument?> document)
    {
        this.document=document;
        var start=new Button {Content="开始进程内试玩"}; var stop=new Button {Content="停止试玩"};
        var top=new WrapPanel {Children={new TextBlock {Text="种子"},seed,seats,external,executable,start,stop}};
        ControlButton("暂停",()=>Send(new("pause"))); ControlButton("继续",()=>Send(new("resume"))); ControlButton("单步 CPU",()=>Send(new("step",frame?.Revision ?? -1)));
        ControlButton("重放当前日志",()=>Send(new("replay")));
        top.Children.Add(observer); top.Children.Add(reveal);
        ControlButton("切换观察身份",()=>Send(new("observe",Seat:observer.SelectedItem as string,Authority:reveal.IsChecked==true)));
        top.Children.Add(logPath); top.Children.Add(authorityLog);
        ControlButton("保存日志",async ()=> { if(session==null) return; var reply=await session.SendAsync(new("recording")); var path=Path.GetFullPath(logPath.Text ?? ""); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,Wire.Json(reply.Recording)); status.Text="日志已保存："+path; });
        ControlButton("打开并重放",()=>Send(new("replay",Recording:Wire.Read<SessionRecording>(File.ReadAllText(logPath.Text ?? "")))));
        authorityLog.IsCheckedChanged+=(_,_)=>ShowTimeline();
        var side=new DockPanel {Width=280}; DockPanel.SetDock(status,Dock.Top); side.Children.Add(status); side.Children.Add(new ScrollViewer {Content=actions});
        var root=new DockPanel(); DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(timeline,Dock.Bottom); DockPanel.SetDock(side,Dock.Right); root.Children.Add(top); root.Children.Add(timeline); root.Children.Add(side); root.Children.Add(table); Content=root;
        start.Click+=async (_,_)=>await Start(); stop.Click+=(_,_)=>Stop();
        table.Gesture+=async g=> { if(frame!=null) await Send(new("submit",frame.Revision,PlayerSession.Resolve(frame.Actions,new(g.Kind,g.Entity,g.Target,g.X,g.Y)))); };
        table.RenderingError+=error=> { Stop(); status.Text=error; };
        timer.Tick+=async (_,_)=> { if(frame is {Cpu:true,View.Ended:false,Fault:null,Paused:false,IsReplay:false} && !busy) await Send(new("cpu",frame.Revision)); };
        void ControlButton(string label,Func<Task> action)
        {
            var button=new Button {Content=label}; button.Click+=async (_,_)=> { try { await action(); } catch(Exception e) {status.Text=e.Message;} }; top.Children.Add(button);
        }
    }
    public void DocumentChanged()
    {
        Stop(); cpuSeats.Clear();
        try
        {
            SyncSeats(document()?.Snapshot()); reveal.IsChecked=false;
        }
        catch(Exception e) {status.Text=e.Message;}
    }
    private void SyncSeats(LoadedModule? current)
    {
        var selected=observer.SelectedItem as string;
        var previous=cpuSeats.ToDictionary(s=>s.Key,s=>s.Value.IsChecked);
        seats.Children.Clear(); cpuSeats.Clear();
        foreach(var seat in current?.Initial.Seats ?? [])
        { var check=new CheckBox {Content=seat.Id+" CPU",IsChecked=previous.GetValueOrDefault(seat.Id,seat.Controller=="cpu"),Margin=new Thickness(6)}; cpuSeats.Add(seat.Id,check); seats.Children.Add(check); }
        observer.ItemsSource=cpuSeats.Keys.ToArray(); observer.SelectedItem=selected!=null && cpuSeats.ContainsKey(selected)?selected:cpuSeats.Keys.FirstOrDefault();
    }
    private async Task Start()
    {
        Stop(); var current=generation;
        try
        {
            if(!uint.TryParse(seed.Text,out var number)) throw new InvalidDataException("种子必须是无符号整数");
            module=document()?.Snapshot() ?? throw new InvalidOperationException("请打开模块");
            SyncSeats(module);
            var controllers=cpuSeats.ToDictionary(s=>s.Key,s=>s.Value.IsChecked==true?"cpu":"human");
            var observer=module.Initial.Seats.FirstOrDefault(s=>controllers.GetValueOrDefault(s.Id)=="human")?.Id ?? module.Initial.Seats[0].Id;
            var created=external.IsChecked==true ? (IDebugSession)await ExternalDebugSession.Launch(executable.Text ?? "",module,number,controllers,observer) : new InProcessDebugSession(module,number,controllers,observer);
            if(current!=generation) {created.Dispose();return;} session=created;
            await Send(new("frame")); if(current==generation) timer.Start();
        }
        catch(Exception e) {status.Text=e.Message;}
    }
    private async Task Send(DebugRequest request)
    {
        if(session==null) return;
        if(busy) { if(request.Operation=="pause") pendingPause=true; return; }
        busy=true; table.InputEnabled=false; actions.IsEnabled=false; var current=generation;
        try { var reply=await session.SendAsync(request); if(current!=generation) return; lastReply=reply; frame=reply.Frame; ShowTimeline(); Refresh(reply.Message.Length>0?reply.Message:reply.Result?.Reason ?? ""); }
        catch(Exception e) { if(current==generation) { timer.Stop(); status.Text="试玩故障："+e.Message; } }
        finally
        {
            if(current==generation)
            {
                busy=false; actions.IsEnabled=true; table.InputEnabled=frame is {Cpu:false,View.Ended:false,Fault:null,IsReplay:false};
                if(pendingPause) { pendingPause=false; await Send(new("pause")); }
            }
        }
    }
    private void ShowTimeline()
    {
        if(lastReply==null) return;
        timeline.Text=authorityLog.IsChecked==true ? "作者权威日志（不提供给玩家或脚本）\n"+string.Join("\n",(lastReply.Timeline ?? []).Select(e=>$"#{e.Submission.Sequence} {e.Submission.Actor} {Wire.Json(e.Submission.Command)} → {e.Submission.Reason}"))+"\n"+string.Join("\n",(lastReply.Diagnostics ?? []).Select(d=>$"#{d.Sequence} {d.Entry} {d.Message}")) : "玩家可读记录\n"+string.Join("\n",(lastReply.PlayerLog ?? []).Select(e=>$"#{e.Sequence} {e.Actor} {e.Reason}"));
    }
    private void Refresh(string message)
    {
        if(frame==null || module==null) return;
        var view=frame.View;
        if(lastReply?.AuthorityState is {} authority)
        {
            var state=Wire.Copy(authority); foreach(var entity in state.Entities.Values) {entity.Visibility="public";entity.FaceUp=true;}
            view=new GameEngine(state).Project(frame.View.Seat);
        }
        var resources=frame.IsReplay && lastReply?.Recording is {} history?history.Versions[history.Log.ModuleFingerprint].ResourceData:module.ResourceData;
        table.Show(TableScene.From(view,resources));
        status.Text=$"试玩 · {frame.View.Seat} | 当前 {frame.View.CurrentSeat}\n{frame.View.Phase} · {frame.View.Turn} · {(frame.Paused?"已暂停":"运行中")}\n"+message+"\n"+(frame.View.Ended?"结算："+string.Join(",",frame.View.Winners):frame.Fault?.Detail ?? "");
        if(lastReply?.AuthorityState!=null) status.Text="⚠ 作者全信息调试 · 隐藏真值\n"+status.Text;
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
        var current=generation; table.InputEnabled=false; actions.IsEnabled=false;
        try { var reply=await session.SendAsync(new("reload",Module:candidate)); if(current!=generation) return "试玩已停止，忽略旧会话的重载结果。"; lastReply=reply; frame=reply.Frame; ShowTimeline(); Refresh(reply.Message); return reply.Message; }
        finally { if(current==generation) {busy=false; actions.IsEnabled=true; table.InputEnabled=frame is {Cpu:false,View.Ended:false,Fault:null,IsReplay:false}; if(pendingPause) {pendingPause=false;await Send(new("pause"));} } }
    }
    public void Stop() { generation++; timer.Stop(); session?.Dispose(); session=null; frame=null; lastReply=null; busy=false; pendingPause=false; timeline.Text=""; actions.Children.Clear(); actions.IsEnabled=true; table.InputEnabled=false; table.Show(new([],new Dictionary<string,byte[]>())); status.Text="已停止；作者初始局面保持不变。"; }
    public void Dispose() { Stop(); table.Dispose(); }
}
