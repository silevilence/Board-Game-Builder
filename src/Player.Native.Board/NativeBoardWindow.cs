using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Player;

/// <summary>A fixed board application. The selected plugins own rules, CPU decisions and rendering.</summary>
public sealed class NativeBoardWindow : Window
{
    private readonly ApplicationCatalog catalog;
    private readonly GameContent content;
    private readonly GraphicsConfiguration graphics;
    private readonly BoardApplication board;
    private readonly StackPanel actions=new(){Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};
    private readonly TextBlock status=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly ContentControl surface=new();
    private readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private Task pending=Task.CompletedTask;
    private readonly CancellationTokenSource lifetime=new();
    private bool closing,released;
    private long revision=-1;
    private string seat="";
    private string[] humans=[];
    private Task viewportShutdown=Task.CompletedTask;
    public string? WaitingFor {get;private set;}
    public string? VisibleSeat=>Table==null?null:humans.Length==0?SeatView.AudienceSeat:seat;
    public RuntimeHost? Instance {get;private set;}
    public IGameSession? Session {get;private set;}
    public ApplicationPump? Pump {get;private set;}
    public InteractiveBoard? Table {get;private set;}
    public string? Error {get;private set;}
    public Task Ready=>pending;
    public NativeBoardWindow(ApplicationCatalog catalog,GameContent content,GraphicsConfiguration graphics,string title)
    {
        this.catalog=catalog;this.content=content;this.graphics=graphics;Title=title;Width=1000;Height=740;
        var selected=content.Identity.Startup.Plugins.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        board=new(sessionId:catalog.Content.Plugins.Single(p=>selected.Contains(p.Id) && p.Provides.Contains("session")).Id);
        var restart=new Button{Content="重新开局"};restart.Click+=async(_,_)=>await Restart();
        var record=new Button{Content="保存并校验记录"};record.Click+=async(_,_)=>await SaveRecording();
        var top=new StackPanel{Margin=new(12),Spacing=8,Children={new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={restart,record}},status,actions}};
        var layout=new DockPanel();DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);layout.Children.Add(surface);Content=layout;
        Opened+=async(_,_)=>await Restart();
        refresh.Tick+=(_,_)=>Refresh();
        Closing+=async(_,eventArgs)=>
        {
            if(released)return;eventArgs.Cancel=true;if(closing)return;closing=true;refresh.Stop();lifetime.Cancel();
            try{await pending;}catch(Exception error){Error=error.Message;}
            try{await Release();}catch(Exception error){Error=error.Message;}
            finally{released=true;lifetime.Dispose();Close();}
        };
    }
    public Task Restart()
    {
        if(closing || !pending.IsCompleted)return pending;
        pending=Open();return pending;
    }
    public Task SaveRecording()
    {
        if(closing || !pending.IsCompleted)return pending;
        pending=SaveRecordingCore();return pending;
    }
    public Task ConfirmHandoff()
    {
        if(closing || !pending.IsCompleted || WaitingFor==null)return pending;
        pending=TakeSeat(WaitingFor);return pending;
    }
    private async Task TakeSeat(string next)
    {
        try
        {
            await viewportShutdown;
            if(closing || Session==null || WaitingFor!=next || Session.CurrentSeat!=next || Session.IsCpu || Session.Ended)return;
            seat=next;WaitingFor=null;CreateTable();revision=-1;Refresh();
        }
        catch(Exception error){ShowError(error);}
    }
    private async Task SaveRecordingCore()
    {
        try
        {
            if(Pump==null || Session==null)return;await Pump.SetPausedAsync(true);
            var text=RecordingCodec.Write(Session.ExportRecording());ValidateRecording(text);
            var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","recordings",content.Fingerprint);
            Directory.CreateDirectory(directory);var file=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");
            var temporary=file+".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary,text,lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();File.Move(temporary,file);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            if(!closing)status.Text="记录已保存并通过回放校验："+file;
        }
        catch(OperationCanceledException) when(closing){}
        catch(Exception error){ShowError(error);}
        finally{if(Pump!=null && !closing)await Pump.SetPausedAsync(false);}
    }
    private async Task Open()
    {
        try
        {
            refresh.Stop();await Release();if(closing)return;
            Instance=await catalog.StartAsync(content);if(closing)return;
            Session=Instance.Service<IGameSession>("session");var module=board.Module(content);
            var controllers=board.Controllers(content);
            humans=module.Initial.Seats.Where(s=>controllers[s.Id]=="human").Select(s=>s.Id).ToArray();
            seat=humans.Length==1?humans[0]:"";WaitingFor=null;
            if(humans.Length<=1)CreateTable();
            revision=-1;Error=null;Refresh();refresh.Start();Pump=new(Instance.Driver);_=Observe(Pump);
        }
        catch(Exception error){ShowError(error);await Release();}
    }
    private async Task Observe(ApplicationPump pump)
    {try{await pump.Completion;}catch(Exception error){if(!closing)await Dispatcher.UIThread.InvokeAsync(()=>ShowError(error));}}
    private void CreateTable()
    {
        var definitions=catalog.Content.Plugins.Where(p=>p.Execution.Scope==RuntimeScope.Viewport).ToArray();
        var selections=content.Identity.Startup.Plugins.Where(s=>definitions.Any(d=>d.Id==s.Id)).ToArray();
        Table=new(graphics,BoardRenderContract.Require(content.Identity.Startup),new(selections,definitions));
        Table.RenderingError+=message=>ShowError(new InvalidOperationException(message));
        Table.Shortcut+=async key=>{if(key is "restart" or "new-game")await Restart();};
        surface.Content=Table;
    }
    private void HideTable()
    {
        // Destroy the prior projection and its animation cache before another seat can take over.
        var table=Table;Table=null;table?.Dispose();surface.Content=null;
        if(table!=null)viewportShutdown=Task.WhenAll(viewportShutdown,table.Shutdown);
        actions.Children.Clear();
    }
    private async Task Release()
    {
        var pump=Pump;Pump=null;try{if(pump!=null)await pump.DisposeAsync();}
        finally
        {
            WaitingFor=null;HideTable();
            try{await viewportShutdown;}
            finally{var instance=Instance;Instance=null;Session=null;if(instance!=null)await instance.DisposeAsync();}
        }
    }
    private void ShowError(Exception error){Error=error.Message;status.Text=error.Message;if(Instance!=null)Instance.Driver.Paused=true;}
    public void Submit(Command command)
    {
        Refresh();
        if(Session==null || Instance==null || closing || Table==null || WaitingFor!=null || humans.Length==0 || Session.IsCpu || Session.CurrentSeat!=seat)return;
        Instance.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(Session.ExportAuthorityLog().EntryCount,seat,command),NativeBoardJson.Default.BoardUpdateRequest));
    }
    public void Refresh()
    {
        if(Session==null || closing)return;
        try
        {
            if(Session.Fault is {} fault){status.Text=humans.Length==0?"规则或 CPU 执行失败，请重新开局。":fault.ToString();return;}
            var current=Session.ExportAuthorityLog().EntryCount;if(current==revision)return;revision=current;
            if(humans.Length>1 && (seat!=Session.CurrentSeat || Session.IsCpu || Table==null))
            {
                HideTable();seat="";
                WaitingFor=!Session.IsCpu && !Session.Ended?Session.CurrentSeat:null;
                status.Text=WaitingFor is {} next?$"请交给席位 {next}；确认接手后显示其牌桌。":Session.Ended?"对局已结束。":"CPU 正在行动；牌桌已隐藏。";
                if(WaitingFor is {} waiting)
                {
                    var take=new Button{Content=$"我是 {waiting}，确认接手"};take.Click+=async(_,_)=>await ConfirmHandoff();actions.Children.Add(take);
                }
                return;
            }
            if(Table==null)return;
            var view=humans.Length==0?GameEngine.ProjectAudienceSnapshot(Session.ExportAuthorityState()):Session.View(seat);
            Table.Show(TableScene.From(view,board.Module(content).Resources));
            status.Text=$"席位 {view.Seat} · 当前 {view.CurrentSeat} · 步数 {Session.Steps}"+(Session.Ended?" · 已结束":"");
            actions.Children.Clear();
            if(humans.Length>0 && !Session.Ended && !Session.IsCpu)foreach(var offer in Session.Actions(seat))
            {
                var button=new Button{Content=offer.Label};button.Click+=(_,_)=>Submit(offer.Command);actions.Children.Add(button);
            }
        }
        catch(Exception error){ShowError(error);}
    }
    public void ValidateRecording(string text)
    {
        var replay=RecordingCodec.Read(text).Replay(module=>board.Validate(content,module));
        if(Session==null || Wire.Json(replay.ExportAuthorityState())!=Wire.Json(Session.ExportAuthorityState()))throw new InvalidDataException("Recording replay differs from the published session");
    }
}

public static class NativeBoardEntry
{
    public static int Run(ApprovedApplicationPlugin[] plugins,string source,string title)
    {
        var catalog=new ApplicationCatalog(plugins);var content=catalog.Load(source);
        return NativeApplication.Run(()=>new NativeBoardWindow(catalog,content,NativeApplication.Graphics(catalog,content),title));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BoardUpdateRequest))]
internal partial class NativeBoardJson:JsonSerializerContext;
