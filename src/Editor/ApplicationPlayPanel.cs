using Avalonia.Controls;
using BoardGame.Rendering;

namespace BoardGame.Editor;

public sealed class ApplicationPlayPanel : UserControl,IAsyncDisposable
{
    private readonly ApplicationSessions sessions;
    private readonly EditorViewports views;
    private readonly ContentControl content=new();
    private readonly TextBlock status=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private BoardGame.Runtime.RuntimeHost? instance;
    private ApplicationViewport? viewport;
    private Task shutdown=Task.CompletedTask;
    private readonly Button pause=new(){Content="暂停"},resume=new(){Content="继续"},step=new(){Content="单步 1/60 秒"};
    public ApplicationPlayPanel(EditorPluginScope scope)
    {
        sessions=scope.Service<ApplicationSessions>("sessions");views=scope.Service<EditorViewports>("viewports");
        scope.OwnAsync(this);
        var start=new Button{Content="开始应用试玩"};var stop=new Button{Content="停止"};
        var external=new CheckBox{Content="独立 Player",IsChecked=sessions.External};
        var executable=new TextBox{Text=sessions.Executable,Width=280};
        external.IsCheckedChanged+=(_,_)=>sessions.External=external.IsChecked==true;
        executable.TextChanged+=(_,_)=>sessions.Executable=executable.Text ?? "";
        start.Click+=async(_,_)=>await sessions.Start();stop.Click+=(_,_)=>sessions.Stop();
        pause.Click+=async(_,_)=>await sessions.Send("pause");resume.Click+=async(_,_)=>await sessions.Send("resume");step.Click+=async(_,_)=>await sessions.Send("step");
        var top=new WrapPanel{Children={start,stop,external,executable,pause,resume,step}};
        var root=new DockPanel();DockPanel.SetDock(top,Dock.Top);DockPanel.SetDock(status,Dock.Top);root.Children.Add(top);root.Children.Add(status);root.Children.Add(content);Content=root;
        sessions.Changed+=Refresh;Refresh();
    }
    private void Refresh()
    {
        pause.IsEnabled=sessions.Active && !sessions.Busy && sessions.State?.Paused==false;
        resume.IsEnabled=step.IsEnabled=sessions.Active && !sessions.Busy && sessions.State?.Paused==true;
        status.Text=sessions.Status+(sessions.State is {} state?$" · tick {state.Tick} · {state.SimulationMilliseconds:0.0} ms":"")+
            (sessions.SourceView is {} source?$"\n试玩来源版本：{source.Source.Version}（当前内存快照，未保存）":"");
        if(ReferenceEquals(instance,sessions.Instance))return;
        ReleaseViewport();instance=sessions.Instance;
        if(instance!=null){viewport=views.Create(instance);viewport.ApplicationError+=error=>status.Text="图形故障："+error+"；运行实例保留，可停止或重建视口。";content.Content=viewport;}
    }
    private void ReleaseViewport()
    {if(viewport!=null){viewport.Dispose();shutdown=Task.WhenAll(shutdown,viewport.Shutdown);viewport=null;}content.Content=null;}
    public async ValueTask DisposeAsync(){sessions.Changed-=Refresh;ReleaseViewport();await shutdown;}
}
