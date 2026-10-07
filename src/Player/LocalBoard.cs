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
    internal LocalTableFrame Frame=>session.Frame;
    internal int LiveTables=>tables.Count;
    internal int RenderedFrames=>tables.Sum(t=>t.RenderedFrames);
    public static async Task<LocalBoard> Create(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,LocalTableMode mode)
    {
        var session=await Task.Run(()=>new LocalTableSession(module,seed,controllers,mode));
        try {return new(module,session);}catch {session.Dispose();throw;}
    }
    private LocalBoard(LoadedModule module,LocalTableSession session)
    {
        this.module=module;this.session=session;
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
        if(frame.Fault!=null) header.Children.Add(new TextBlock {Text="对局故障："+frame.Fault.Code+" · "+frame.Fault.Detail});
        foreach(var seat in frame.CanTake ?? [])
        {
            if(seat==frame.WaitingFor || frame.Views.Any(v=>v.View.Seat==seat)) continue;
            var handoff=new Button {Content="交接至 "+seat+"（同时收集／处理缺席）"};
            handoff.Click+=async (_,_)=>await Run(async ()=> {await session.HandoffAsync(frame.Revision,seat);});header.Children.Add(handoff);
        }
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
            var table=new InteractiveTable {InputEnabled=!seat.Cpu && !seat.View.Ended && seat.Fault==null && seat.Actions.Count>0};tables.Add(table);
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
    internal async Task VerifyStep()
    {
        var frame=session.Frame;
        if(frame.WaitingFor is {} next) await Run(async ()=> {await session.TakeSeatAsync(frame.Revision,next);});
        else
        {
            var view=frame.Views.First(v=>v.Actions.Count>0);
            await Run(async ()=> {await session.SubmitAsync(frame.Revision,view.View.Seat,new("action",Action:view.Actions[0].Id));});
        }
    }
    public void Dispose() {disposed=true;session.Dispose();Clear();}
}
