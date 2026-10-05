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
    private readonly PlayerSession session;
    private readonly LoadedModule module;
    private bool disposed;
    private bool pumping;
    private string? hostError;
    private SoundEffect? tone;
    public SessionFrame Frame=>session.Frame;
    public PlayBoard(PlayerSession session,LoadedModule module)
    {
        this.session=session; this.module=module;
        var side=new DockPanel { Width=250 }; DockPanel.SetDock(status,Dock.Top); side.Children.Add(status);
        DockPanel.SetDock(sound,Dock.Bottom); side.Children.Add(sound); side.Children.Add(new ScrollViewer { Content=actions });
        var panel=new DockPanel(); DockPanel.SetDock(side,Dock.Right); panel.Children.Add(side); panel.Children.Add(table); Content=panel;
        table.Gesture+=g=> { _=Act(new(g.Kind,g.Entity,g.Target,g.X,g.Y),session.Frame.Revision); };
        table.RenderingError+=error=> { session.Stop(); hostError="渲染设备故障："+error+"\n请结束对局后重新开始。"; Refresh(); };
        Refresh();
    }
    private void Refresh()
    {
        if(disposed) return;
        var frame=session.Frame; var v=frame.View;
        table.Show(TableScene.From(v,module.ResourceData)); table.InputEnabled=hostError==null && !session.Busy && !v.Ended && frame.Fault==null && !frame.Cpu;
        status.Text=hostError ?? $"席位 {v.Seat} · 当前 {v.CurrentSeat}\n阶段 {v.Phase} · 行动 {v.Turn}\n"+
            (frame.Fault!=null?$"对局故障：{frame.Fault.Code}\n{frame.Fault.Detail}":v.Ended?$"对局结束\n胜者：{string.Join(", ",v.Winners)}":session.LastMessage);
        actions.Children.Clear();
        foreach(var offer in hostError==null?frame.Actions:[])
        {
            var revision=frame.Revision; Add(offer.Label,()=>Act(new("action",Action:offer.Id),revision));
        }
        if(table.InputEnabled)
        {
            Add("翻面所选对象",()=>Act(new("flip",table.Selected),frame.Revision));
            Add("从所选牌堆抽牌",()=>Act(new("draw",table.Selected,v.Entities.FirstOrDefault(e=>e.IsContainer && e.Owner==v.Seat)?.Id),frame.Revision));
        }
        if(frame.Cpu && !v.Ended && frame.Fault==null) status.Text+="\nCPU 正在行动…";
    }
    private void Add(string label,Func<Task> action)
    {
        var button=new Button { Content=label, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch };
        button.Click+=async (_,_)=>await action(); actions.Children.Add(button);
    }
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
    private void SetBusy(string message) { status.Text=message; actions.Children.Clear(); table.InputEnabled=false; }
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
