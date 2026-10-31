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
    private bool closing,released;
    private long revision=-1;
    private string seat="";
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
        var record=new Button{Content="保存并校验记录"};record.Click+=async(_,_)=>
        {
            try
            {
                if(Pump==null || Session==null)return;await Pump.SetPausedAsync(true);
                var text=RecordingCodec.Write(Session.ExportRecording());ValidateRecording(text);
                var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","recordings",content.Fingerprint);
                Directory.CreateDirectory(directory);var file=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".json");
                await File.WriteAllTextAsync(file,text);status.Text="记录已保存并通过回放校验："+file;
            }
            catch(Exception error){ShowError(error);}
            finally{if(Pump!=null && !closing)await Pump.SetPausedAsync(false);}
        };
        var top=new StackPanel{Margin=new(12),Spacing=8,Children={new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,Children={restart,record}},status,actions}};
        var layout=new DockPanel();DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);layout.Children.Add(surface);Content=layout;
        Opened+=async(_,_)=>await Restart();
        refresh.Tick+=(_,_)=>Refresh();
        Closing+=async(_,eventArgs)=>
        {
            if(released)return;eventArgs.Cancel=true;if(closing)return;closing=true;refresh.Stop();
            try{await pending;await Release();}catch(Exception error){Error=error.Message;}
            finally{released=true;Close();}
        };
    }
    public Task Restart()
    {
        if(closing || !pending.IsCompleted)return pending;
        pending=Open();return pending;
    }
    private async Task Open()
    {
        try
        {
            refresh.Stop();await Release();if(closing)return;
            Instance=await catalog.StartAsync(content);if(closing)return;
            Session=Instance.Service<IGameSession>("session");var module=board.Module(content);
            var controllers=board.Controllers(content);
            seat=module.Initial.Seats.FirstOrDefault(s=>controllers[s.Id]=="human")?.Id ?? module.Initial.Seats[0].Id;
            var definitions=catalog.Content.Plugins.Where(p=>p.Execution.Scope==RuntimeScope.Viewport).ToArray();
            var selections=content.Identity.Startup.Plugins.Where(s=>definitions.Any(d=>d.Id==s.Id)).ToArray();
            Table=new(graphics,BoardRenderContract.Require(content.Identity.Startup),new(selections,definitions));
            Table.RenderingError+=message=>ShowError(new InvalidOperationException(message));
            Table.Shortcut+=async key=>{if(key is "restart" or "new-game")await Restart();};
            surface.Content=Table;revision=-1;Error=null;Refresh();refresh.Start();Pump=new(Instance.Driver);_=Observe(Pump);
        }
        catch(Exception error){ShowError(error);await Release();}
    }
    private async Task Observe(ApplicationPump pump)
    {try{await pump.Completion;}catch(Exception error){if(!closing)await Dispatcher.UIThread.InvokeAsync(()=>ShowError(error));}}
    private async Task Release()
    {
        var pump=Pump;Pump=null;try{if(pump!=null)await pump.DisposeAsync();}
        finally
        {
            var table=Table;Table=null;table?.Dispose();surface.Content=null;
            try{if(table!=null)await table.Shutdown;}
            finally{var instance=Instance;Instance=null;Session=null;if(instance!=null)await instance.DisposeAsync();}
        }
    }
    private void ShowError(Exception error){Error=error.Message;status.Text=error.Message;if(Instance!=null)Instance.Driver.Paused=true;}
    public void Submit(Command command)
    {
        if(Session==null || Instance==null || closing)return;
        Instance.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(Session.ExportAuthorityLog().EntryCount,seat,command),NativeBoardJson.Default.BoardUpdateRequest));
    }
    public void Refresh()
    {
        if(Session==null || Table==null || closing)return;
        try
        {
            if(Session.Fault is {} fault){status.Text=fault.ToString();return;}
            var current=Session.ExportAuthorityLog().EntryCount;if(current==revision)return;revision=current;
            var view=Session.View(seat);Table.Show(TableScene.From(view,board.Module(content).Resources));
            status.Text=$"席位 {seat} · 当前 {view.CurrentSeat} · 步数 {Session.Steps}"+(Session.Ended?" · 已结束":"");
            actions.Children.Clear();
            if(!Session.Ended && !Session.IsCpu)foreach(var offer in Session.Actions(seat))
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
