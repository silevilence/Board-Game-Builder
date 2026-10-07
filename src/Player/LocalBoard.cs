using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Player;

public sealed class LocalBoard : UserControl,IDisposable
{
    private readonly LocalTableSession session;
    private readonly LoadedModule module;
    private readonly StackPanel header=new() {Spacing=8};
    private readonly Grid views=new();
    private readonly List<InteractiveTable> tables=[];
    private bool disposed,busy;
    public LocalBoard(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,LocalTableMode mode)
    {
        this.module=module;session=new(module,seed,controllers,mode);
        var root=new DockPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(views);Content=root;
        Refresh();
    }
    private void Clear()
    {
        foreach(var table in tables) {table.InputEnabled=false;table.Show(new([],new Dictionary<string,byte[]>()));table.Dispose();}
        tables.Clear();views.Children.Clear();views.ColumnDefinitions.Clear();header.Children.Clear();
    }
    private void Refresh()
    {
        Clear();if(disposed) return;
        header.Children.Add(new TextBlock {Text="本地多人 · 同屏内容可被旁观；身份投影隔离数据，不能阻止旁观屏幕。"});
        var frame=session.Frame;
        if(frame.WaitingFor is {} next)
        {
            var take=new Button {Content="请交给 "+next+" · 我已接手，显示本席位"};
            take.Click+=async (_,_)=>await Run(async ()=> {await session.TakeSeatAsync(frame.Revision,next);});header.Children.Add(take);
        }
        foreach(var seat in frame.Views)
        {
            var column=tables.Count;views.ColumnDefinitions.Add(new ColumnDefinition());
            var panel=new DockPanel();Grid.SetColumn(panel,column);views.Children.Add(panel);
            var controls=new StackPanel {Spacing=6};DockPanel.SetDock(controls,Dock.Top);panel.Children.Add(controls);
            controls.Children.Add(new TextBlock {Text=$"控制席位：{seat.View.Seat} · 当前：{seat.View.CurrentSeat} · {seat.View.Phase}"});
            if(seat.View.Ended) controls.Children.Add(new TextBlock {Text="结算："+string.Join(", ",seat.View.Winners)});
            if(seat.Fault!=null) controls.Children.Add(new TextBlock {Text=seat.Fault.Detail});
            foreach(var offer in seat.Actions)
            {
                var button=new Button {Content=offer.Label};
                button.Click+=async (_,_)=>await Run(async ()=> {await session.SubmitAsync(frame.Revision,seat.View.Seat,new("action",Action:offer.Id));});
                controls.Children.Add(button);
            }
            var table=new InteractiveTable {InputEnabled=!seat.Cpu && !seat.View.Ended && seat.Fault==null && seat.View.CurrentSeat==seat.View.Seat};tables.Add(table);
            table.Show(TableScene.From(seat.View,module.ResourceData));panel.Children.Add(table);
            table.Gesture+=g=>_=Run(async ()=> {await session.SubmitAsync(frame.Revision,seat.View.Seat,new(g.Kind,g.Entity,g.Target,g.X,g.Y));});
            table.RenderingError+=error=> {Clear();header.Children.Add(new TextBlock {Text=error});session.Dispose();};
        }
    }
    private async Task Run(Func<Task> action)
    {
        if(disposed || busy) return;
        busy=true;Clear();
        try {await action();while(!disposed && session.Frame.Cpu && !session.Frame.Ended) if(!await session.StepCpuAsync()) break;}
        catch(Exception error) {session.Dispose();Refresh();header.Children.Add(new TextBlock {Text="对局故障："+error.Message});return;}
        finally {busy=false;}
        if(!disposed) Refresh();
    }
    public Task PumpCpu()=>Run(()=>Task.CompletedTask);
    public void Dispose() {disposed=true;session.Dispose();Clear();}
}
