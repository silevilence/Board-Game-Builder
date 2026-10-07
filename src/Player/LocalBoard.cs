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
    private bool showActions;
    private readonly List<ScrollViewer> sidebars=[];
    internal LocalTableFrame Frame=>session.Frame;
    internal int LiveTables=>tables.Count;
    internal int RenderedFrames=>tables.Sum(t=>t.RenderedFrames);
    internal bool ActionsVisible=>showActions;
    public static async Task<LocalBoard> Create(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,LocalTableMode mode,bool showActions=true)
    {
        var session=await Task.Run(()=>new LocalTableSession(module,seed,controllers,mode));
        try {return new(module,session,showActions);}catch {session.Dispose();throw;}
    }
    private LocalBoard(LoadedModule module,LocalTableSession session,bool showActions)
    {
        this.module=module;this.session=session;this.showActions=showActions;
        var root=new DockPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(views);Content=root;
        Refresh();
    }
    private void Clear()
    {
        foreach(var table in tables) {table.InputEnabled=false;table.Show(new([],new Dictionary<string,byte[]>()));table.Dispose();}
        tables.Clear();sidebars.Clear();views.Children.Clear();views.ColumnDefinitions.Clear();header.Children.Clear();
    }
    private void Refresh()
    {
        Clear();if(disposed) return;
        var frame=session.Frame;
        if(frame.Ended) header.Children.Add(new TextBlock {Text="对局结束 · 胜者："+string.Join("、",frame.Winners ?? []),FontSize=22,Margin=new Avalonia.Thickness(12)});
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
            var controls=new StackPanel {Spacing=6,Margin=new Avalonia.Thickness(8)};
            var sidebar=new ScrollViewer {Content=controls,Width=270,IsVisible=showActions};sidebars.Add(sidebar);DockPanel.SetDock(sidebar,Dock.Right);panel.Children.Add(sidebar);
            var identity=new TextBlock {Text="控制席位："+seat.View.Seat,Margin=new Avalonia.Thickness(8,4)};DockPanel.SetDock(identity,Dock.Top);panel.Children.Add(identity);
            controls.Children.Add(new TextBlock {Text=$"控制席位：{seat.View.Seat} · 当前：{seat.View.CurrentSeat} · {seat.View.Phase}"});
            if(seat.View.Ended) controls.Children.Add(new TextBlock {Text="结算："+string.Join(", ",seat.View.Winners)});
            if(seat.Fault!=null) controls.Children.Add(new TextBlock {Text=seat.Fault.Detail});
            foreach(var offer in seat.Actions)
            {
                var button=new Button {Content=new TextBlock {Text=offer.Label,TextWrapping=Avalonia.Media.TextWrapping.Wrap},HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
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
    public void Fit() {foreach(var table in tables) table.Fit();}
    public void ToggleActions() {showActions=!showActions;foreach(var sidebar in sidebars) sidebar.IsVisible=showActions;}
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
