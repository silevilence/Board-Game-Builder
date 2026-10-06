using Avalonia;
using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed class PlayPanel : UserControl,IDisposable
{
    private readonly SessionController controller;
    internal InteractiveTable Table { get; }
    private readonly StackPanel actions=new() {Width=260,Spacing=6};
    private readonly TextBlock status=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBox timeline=new() {IsReadOnly=true,AcceptsReturn=true,Height=170};
    private readonly TextBox seed;
    private readonly StackPanel seats=new() {Orientation=Avalonia.Layout.Orientation.Horizontal};
    private readonly ComboBox observer=new() {Width=100};
    private readonly CheckBox reveal;
    private readonly Dictionary<string,CheckBox> seatControls=[];
    private readonly Button start=new() {Content="开始试玩"};
    public PlayPanel(EditorPluginScope scope)
    {
        controller=scope.Service<SessionController>("sessions");
        Table=scope.Service<EditorViewports>("viewports").Create(scope);
        seed=new TextBox {Text=controller.Seed,Width=100};
        var external=new CheckBox {Content="独立 Player",IsChecked=controller.External};
        var executable=new TextBox {Text=controller.Executable,Width=260,PlaceholderText="调试 Player.exe 路径"};
        var logPath=new TextBox {Text=controller.LogPath,Width=260};
        var authorityLog=new CheckBox {Content="显示作者命令与 CPU 诊断（含私有数据）",IsChecked=controller.AuthorityLog};
        reveal=new CheckBox {Content="作者全信息调试（包含隐藏真值）",IsChecked=controller.Reveal};
        var stop=new Button {Content="停止试玩"};
        var top=new WrapPanel {Children={new TextBlock {Text="种子"},seed,seats,external,executable,start,stop}};
        seed.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) controller.Seed=seed.Text ?? "";};
        external.IsCheckedChanged+=(_,_)=>controller.External=external.IsChecked==true;
        executable.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) controller.Executable=executable.Text ?? "";};
        logPath.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) controller.LogPath=logPath.Text ?? "";};
        authorityLog.IsCheckedChanged+=(_,_)=> {controller.AuthorityLog=authorityLog.IsChecked==true;Refresh();};
        reveal.IsCheckedChanged+=(_,_)=>controller.Reveal=reveal.IsChecked==true;
        observer.SelectionChanged+=(_,_)=>controller.Observer=observer.SelectedItem as string;
        ControlButton("暂停",()=>controller.Send(new("pause")));
        ControlButton("继续",()=>controller.Send(new("resume")));
        ControlButton("单步 CPU",()=>controller.Send(new("step",controller.Frame?.Revision ?? -1)));
        ControlButton("重放当前日志",()=>controller.Send(new("replay")));
        top.Children.Add(observer);top.Children.Add(reveal);
        ControlButton("切换观察身份",()=>controller.Send(new("observe",Seat:controller.Observer,Authority:controller.Reveal)));
        top.Children.Add(logPath);top.Children.Add(authorityLog);
        ControlButton("保存日志",controller.SaveRecording);
        ControlButton("打开并重放",()=>controller.Send(new("replay",Recording:Wire.Read<SessionRecording>(File.ReadAllText(controller.LogPath)))));
        var side=new DockPanel {Width=280};DockPanel.SetDock(status,Dock.Top);side.Children.Add(status);side.Children.Add(new ScrollViewer {Content=actions});
        var root=new DockPanel();DockPanel.SetDock(top,Dock.Top);DockPanel.SetDock(timeline,Dock.Bottom);DockPanel.SetDock(side,Dock.Right);
        root.Children.Add(top);root.Children.Add(timeline);root.Children.Add(side);root.Children.Add(Table);Content=root;
        start.Click+=async (_,_)=>await controller.Start();stop.Click+=(_,_)=>controller.Stop();
        TableKeyInput.Attach(this,Table.HandleShortcut);
        seed.KeyDown+=async (_,e)=> {if(e.Key==Avalonia.Input.Key.Enter) {e.Handled=true;await controller.Start();}};
        Table.Gesture+=async g=> {if(controller.Frame is {} frame) await controller.Send(new("submit",frame.Revision,PlayerSession.Resolve(frame.Actions,new(g.Kind,g.Entity,g.Target,g.X,g.Y))));};
        Table.Shortcut+=async key=>
        {
            if(key=="choose-seed") {seed.Focus();seed.SelectAll();return;}
            if(key is "restart" or "new-game")
            {
                seed.Text=key=="new-game"?System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue).ToString():controller.ActiveSeed?.ToString() ?? controller.Seed;
                await controller.Start();return;
            }
            if(controller.Frame is not {} frame || controller.Busy) return;
            if(key=="hint") {var hint=frame.Actions.FirstOrDefault(a=>a.Gesture=="drag");Table.Hint(hint?.Command.Entity,hint?.Target);status.Text=hint==null?"没有可提示动作":"合法走法（不保证最优）："+hint.Label;return;}
            await controller.Send(new("submit",frame.Revision,PlayerSession.Resolve(frame.Actions,new("shortcut",Table.Selected,Action:key))));
        };
        Table.RenderingError+=error=> {controller.Stop();status.Text=error;};
        controller.Changed+=Refresh;Refresh();
        void ControlButton(string label,Func<Task> action)
        {
            var button=new Button {Content=label};button.Click+=async (_,_)=> {try {await action();} catch(Exception e) {status.Text=e.Message;}};top.Children.Add(button);
        }
    }
    private void Refresh()
    {
        if(!seatControls.Keys.SequenceEqual(controller.CpuSeats.Keys))
        {
            seats.Children.Clear();seatControls.Clear();
            foreach(var seat in controller.CpuSeats)
            {
                var check=new CheckBox {Content=seat.Key+" CPU",IsChecked=seat.Value,Margin=new Thickness(6)};
                check.IsCheckedChanged+=(_,_)=>controller.CpuSeats[seat.Key]=check.IsChecked==true;seats.Children.Add(check);seatControls[seat.Key]=check;
            }
            var selected=controller.Observer;observer.ItemsSource=controller.CpuSeats.Keys.ToArray();observer.SelectedItem=selected;
        }
        observer.SelectedItem=controller.Observer;reveal.IsChecked=controller.Reveal;
        actions.Children.Clear();start.IsEnabled=!controller.Busy;
        Table.InputEnabled=controller.CanInput;actions.IsEnabled=!controller.Busy;status.Text=controller.Status;
        var reply=controller.Reply;
        timeline.Text=reply==null?"":controller.AuthorityLog
            ? "作者权威日志（不提供给玩家或脚本）\n"+string.Join("\n",(reply.Timeline ?? []).Select(e=>$"#{e.Submission.Sequence} {e.Submission.Actor} {Wire.Json(e.Submission.Command)} → {e.Submission.Reason}"))+"\n"+string.Join("\n",(reply.Diagnostics ?? []).Select(d=>$"#{d.Sequence} {d.Entry} {d.Message}"))
            : "玩家可读记录\n"+string.Join("\n",(reply.PlayerLog ?? []).Select(e=>$"#{e.Sequence} {e.Actor} {e.Reason}"));
        if(controller.Frame is not {} frame || controller.Module is not {} module) {Table.Show(new([],new Dictionary<string,byte[]>()));return;}
        var view=frame.View;
        if(reply?.AuthorityState is {} authority) {var state=Wire.Copy(authority);foreach(var e in state.Entities.Values) {e.Visibility="public";e.FaceUp=true;}view=new GameEngine(state).Project(frame.View.Seat);}
        var resources=frame.IsReplay && reply?.Recording is {} history?history.Versions[history.Log.ModuleFingerprint].ResourceData:module.ResourceData;
        Table.Show(TableScene.From(view,resources));
        status.Text=$"试玩 · {frame.View.Seat} | 当前 {frame.View.CurrentSeat}\n{frame.View.Phase} · {frame.View.Turn} · {(frame.Paused?"已暂停":"运行中")}\n"+controller.Status+"\n"+(frame.View.Ended?"结算："+string.Join(",",frame.View.Winners):frame.Fault?.Detail ?? "");
        if(reply?.AuthorityState!=null) status.Text="⚠ 作者全信息调试 · 隐藏真值\n"+status.Text;
        foreach(var offer in frame.Actions)
        {
            var revision=frame.Revision;var button=new Button {Content=offer.Label};
            button.Click+=async (_,_)=>await controller.Send(new("submit",revision,offer.Command));actions.Children.Add(button);
        }
    }
    public void Dispose() {controller.Changed-=Refresh;Table.Dispose();}
}
