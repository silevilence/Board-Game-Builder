using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;
using Microsoft.Xna.Framework.Audio;
namespace BoardGame.Player;

public sealed class PlayBoard : UserControl,IDisposable
{
    private readonly InteractiveTable table=new();
    private readonly StackPanel actions=new() { Spacing=8, Margin=new Thickness(12) };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(12), FontSize=16 };
    private readonly CheckBox sound=new() { Content="操作音效",IsChecked=false };
    private readonly DockPanel side=new() {Width=250};
    private readonly StackPanel quickActions=new() {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};
    private readonly bool classic;
    private readonly PlayerSession session;
    private readonly LoadedModule module;
    private bool disposed;
    private bool pumping;
    private string? hostError;
    private SoundEffect? tone;
    public SessionFrame Frame=>session.Frame;
    internal bool ActionsVisible=>side.IsVisible;
    internal int RenderedFrames=>table.RenderedFrames;
    internal int CachedImages=>table.CachedImages;
    internal void SaveImage(string path)=>table.SaveImage(path);
    public event Action<string>? HostShortcut;
    public PlayBoard(PlayerSession session,LoadedModule module,bool showActions=true)
    {
        this.session=session; this.module=module;
        classic=TableScene.From(session.Frame.View).ClassicCards;
        var panel=new DockPanel();
        if(classic)
        {
            side.Background=new SolidColorBrush(Color.Parse("#e3e3d9"));
            side.Children.Add(new ScrollViewer {Content=actions});
            status.FontSize=13; status.Margin=new Thickness(10,5); status.VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center;
            var footer=new DockPanel {Background=new SolidColorBrush(Color.Parse("#d4d0c8")),MinHeight=34};
            quickActions.Margin=new Thickness(4); DockPanel.SetDock(quickActions,Dock.Right); footer.Children.Add(quickActions); footer.Children.Add(status);
            DockPanel.SetDock(footer,Dock.Bottom); panel.Children.Add(footer);
        }
        else
        { DockPanel.SetDock(status,Dock.Top); side.Children.Add(status); DockPanel.SetDock(sound,Dock.Bottom); side.Children.Add(sound); side.Children.Add(new ScrollViewer {Content=actions}); }
        side.IsVisible=showActions;
        DockPanel.SetDock(side,Dock.Right); panel.Children.Add(side); panel.Children.Add(table); Content=panel;
        table.Gesture+=g=> { _=Act(new(g.Kind,g.Entity,g.Target,g.X,g.Y),session.Frame.Revision); };
        table.SelectionChanged+=RefreshActions;
        table.Shortcut+=key=>
        {
            if(key is "new-game" or "restart" or "choose-seed") { HostShortcut?.Invoke(key); return; }
            if(key=="hint")
            {
                var hint=session.Frame.Actions.FirstOrDefault(a=>a.Gesture=="drag");
                table.Hint(hint?.Command.Entity,hint?.Target);
                status.Text=hint==null?"当前没有可提示的搬牌动作。":"合法走法（不保证最优）："+hint.Label;
            }
            else _=Act(new("shortcut",table.Selected,Action:key),session.Frame.Revision);
        };
        table.RenderingError+=error=> { session.Stop(); hostError="渲染设备故障："+error+"\n请结束对局后重新开始。"; Refresh(); };
        Refresh();
    }
    private void Refresh()
    {
        if(disposed) return;
        var frame=session.Frame; var v=frame.View;
        table.Show(TableScene.From(v,module.Resources)); table.InputEnabled=hostError==null && !session.Busy && !v.Ended && frame.Fault==null && !frame.Cpu;
        status.Text=hostError ?? $"席位 {v.Seat} · 当前 {v.CurrentSeat}\n阶段 {v.Phase} · 行动 {v.Turn}\n"+
            (frame.Fault!=null?$"对局故障：{frame.Fault.Code}\n{frame.Fault.Detail}":v.Ended?$"对局结束\n胜者：{string.Join(", ",v.Winners)}":session.LastMessage);
        if(classic && hostError==null && frame.Fault==null)
        {
            var cards=v.Entities.Where(e=>e.IsContainer && e.Properties.GetValueOrDefault("viewSuit")>0).Sum(e=>e.Count);
            status.Text=v.Ended?(v.Winners.Count>0?"恭喜，全部收齐！可按 F2 再来一局。":"本局已结束。可按 F2 开始新局。")
                :$"已收 {cards} 张  |  移动 {v.Turn} 次  ·  "+(session.LastMessage.StartsWith("操作被拒绝") || session.LastMessage.StartsWith("当前规则")?session.LastMessage:"双击快速移牌 · 按住右键看牌");
        }
        RefreshActions();
        if(frame.Cpu && !v.Ended && frame.Fault==null) status.Text+="\nCPU 正在行动…";
    }
    private void RefreshActions()
    {
        var frame=session.Frame;
        actions.Children.Clear();
        quickActions.Children.Clear();
        if(classic && hostError==null && !frame.View.Ended && frame.Fault==null)
            foreach(var offer in frame.Actions.Where(a=>a.Shortcuts?.Contains("confirm")==true))
            {
                var revision=frame.Revision; var button=new Button {Content=offer.Label,Padding=new Thickness(12,4)};
                button.Click+=async (_,_)=>await Act(new("action",Action:offer.Id),revision); quickActions.Children.Add(button);
            }
        var dragOffers=frame.Actions.Where(a=>a.Gesture=="drag").ToArray();
        if(dragOffers.Length>0)
            actions.Children.Add(new TextBlock { Text=table.Selected==null?"点选牌，再点目标；也可拖动。双击快速移牌，按住右键看牌。H 提示，Esc 取消。":"所选对象的合法去向：",TextWrapping=TextWrapping.Wrap });
        foreach(var offer in hostError==null?frame.Actions:[])
        {
            if(offer.Gesture=="drag" && offer.Command.Entity!=table.Selected) continue;
            var revision=frame.Revision; Add(offer.Label,()=>Act(new("action",Action:offer.Id),revision));
        }
    }
    private void Add(string label,Func<Task> action)
    {
        var button=new Button { Content=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap}, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch };
        button.Click+=async (_,_)=>await action(); actions.Children.Add(button);
    }
    public void HandleShortcut(string key)=>table.HandleShortcut(key);
    public void Fit()=>table.Fit();
    public void ToggleActions()=>side.IsVisible=!side.IsVisible;
    public void ToggleSound()=>sound.IsChecked=sound.IsChecked!=true;
    public async Task Act(InteractionIntent intent,long revision)
    {
        if(disposed || session.Busy) return;
        SetBusy("正在提交操作…");
        try
        {
            var result=await session.SubmitAsync(revision,intent);
            if(sound.IsChecked==true && !disposed) PlayTone(result.Accepted);
        }
        catch(Exception e) { session.Stop(); hostError="对局故障："+e.Message; }
        finally { Refresh(); }
        await PumpCpu();
    }
    public async Task AdvanceCpu()
    {
        if(disposed || session.Busy) return;
        SetBusy("CPU 正在思考…");
        try { await session.StepCpuAsync(); }
        catch(Exception e) { session.Stop(); hostError="CPU 故障："+e.Message; }
        finally { Refresh(); }
    }
    public async Task PumpCpu()
    {
        if(pumping) return;
        pumping=true;
        try { while(!disposed && hostError==null && Frame.Cpu && !Frame.View.Ended && Frame.Fault==null) await AdvanceCpu(); }
        finally { pumping=false; }
    }
    private void SetBusy(string message) { status.Text=message; actions.Children.Clear(); quickActions.Children.Clear(); table.InputEnabled=false; }
    private void PlayTone(bool accepted)
    {
        try
        {
            tone?.Dispose(); var pcm=new byte[4410];
            for(var i=0;i<pcm.Length/2;i++) { var value=(short)(Math.Sin(i*2*Math.PI*(accepted?660:220)/22050)*2500*(1-i/(pcm.Length/2.0))); pcm[2*i]=(byte)value; pcm[2*i+1]=(byte)(value>>8); }
            tone=new SoundEffect(pcm,22050,AudioChannels.Mono); tone.Play();
        }
        catch(Exception) { sound.IsChecked=false; sound.Content="音效不可用（已静音）"; }
    }
    public void Dispose() { disposed=true; session.Stop(); table.Dispose(); tone?.Dispose(); }
}
