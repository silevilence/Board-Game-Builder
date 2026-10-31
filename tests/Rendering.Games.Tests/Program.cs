#if DESKTOP_GL
using Backend=BoardGame.Rendering.DesktopGlBackend;
#else
using Backend=BoardGame.Rendering.WindowsDxBackend;
#endif
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Runtime;
using BoardGame.View;
using Sample.Motion;

Games.Output=Path.GetFullPath(args.FirstOrDefault() ?? ".scratch/rendering-games.json");
AppBuilder.Configure<Games>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
return Games.Failed?1:0;

public sealed class Games:Avalonia.Application
{
    public static string Output="";public static bool Failed;
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var window=new Window{Title="Runtime graphics verification",Width=920,Height=620};desktop.MainWindow=window;
            window.Opened+=async(_,_)=>await Run(window,desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Until(Func<bool> ready,string message)
    {for(var i=0;i<300;i++){if(ready())return;await Task.Delay(20);}throw new Exception(message);}
    private static nint InputHandle(nint parent)
    {
#if DESKTOP_GL
        var child=GetWindow(parent,5);Check(child!=0,"Missing GL input child");return child;
#else
        return parent;
#endif
    }
    private static async Task Run(Window window,IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errors=new List<string>();var checks=new List<string>();var screenshots=new List<string>();string stateHash="",recordingHash="";
        var backend=Backend.Plugin();var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,1,RuntimeConfiguration.Empty)]),[backend]);
        ApplicationViewport? motionView=null;InteractiveBoard? table=null;RuntimeHost? host=null;ApplicationPump? pump=null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            var motion=new ApplicationCatalog(MotionPlugins.Catalog());host=await motion.StartAsync(motion.Load(Path.Combine(AppContext.BaseDirectory,"motion")),clock:new(TimeSpan.FromMilliseconds(10),20));
            var motionState=host.Service<MotionState>("motion.state");motionView=new(graphics,host);window.Content=motionView;
            await Until(()=>motionView.Frames>2 || motionView.Error!=null,"Motion viewport did not draw");Check(motionView.Error==null,motionView.Error ?? "");
            pump=new(host.Driver);var input=InputHandle(motionView.NativeHandle);
            SendMessage(input,0x100,0x27,0);await Until(()=>motionState.Snapshot.Reached,"Motion target not reached");SendMessage(input,0x101,0x27,0);
            await pump.SetPausedAsync(true);var paused=motionState.Snapshot;var frames=motionView.Frames;
            await Until(()=>motionView.Frames>frames+2,"Paused rendering stopped");Check(motionState.Snapshot==paused,"Render advanced state");
            motionView.SaveImage(Output+".motion.png");screenshots.Add(Output+".motion.png");
            SendMessage(input,0x100,0x20,0);SendMessage(input,0x101,0x20,0);await pump.StepAsync();
            Check(Math.Abs(motionState.Snapshot.X-60)<.001 && !motionState.Snapshot.Reached,"Native key reset differs");
            await pump.DisposeAsync();pump=null;motionView.Dispose();window.Content=null;await motionView.Shutdown;motionView=null;await host.DisposeAsync();host=null;
            checks.Add("motion-native-key-target-reset-and-paused-render");

            var folder=Path.Combine(Path.GetTempPath(),"rendering-board-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"state.json"),State);
            File.WriteAllText(Path.Combine(folder,BoardGame.AppRuntime.GameRuntime.Entry),Startup);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"motion/marker.png"),Path.Combine(folder,"marker.png"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"font/NotoSansCJKsc-Regular.otf"),Path.Combine(folder,"font.otf"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"font/LICENSE.txt"),Path.Combine(folder,"OFL.txt"));
            var board=new BoardApplication(42);var catalog=new ApplicationCatalog(board.Plugins().Append(new(_=>BoardRenderContract.Description)));
            var content=catalog.Load(folder);host=await catalog.StartAsync(content);var session=host.Service<IGameSession>("session");
            var resources=board.Module(content).Resources!;
            table=new(graphics,content.Identity.Startup.Plugins.Single(p=>p.Id==BoardRenderContract.Id));window.Content=table;
            void Show()
            {
                var scene=TableScene.From(session.View("p1"),resources);
                var buttons=session.Actions("p1").Select((a,i)=>new TableItem(null,"ui-button",a.Label,new(30+i*160,320,145,45),Action:a.Id));
                table.Show(scene with {Items=scene.Items.Concat(buttons).ToArray()});
            }
            table.Gesture+=gesture=>
            {
                var command=InteractionResolver.Resolve(session.Actions("p1"),new(gesture.Kind,gesture.Entity,gesture.Target,gesture.X,gesture.Y,gesture.Action));
                host.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(session.ExportAuthorityLog().EntryCount,"p1",command),BoardSessionJson.Default.BoardUpdateRequest));
            };
            table.RenderingError+=errors.Add;Show();await Until(()=>table.RenderedFrames>2 || errors.Count>0,"Board viewport did not draw");Check(errors.Count==0,string.Join(';',errors));
            input=InputHandle(table.NativeHandle);
            for(var step=0;step<3;step++)
            {
                // PlatformViewport starts at the actual monitor DPI; input coordinates are physical pixels.
                var x=(int)((90+step*160)*window.RenderScaling);var y=(int)(340*window.RenderScaling);var point=(nint)((y<<16)|(x&0xffff));
                SendMessage(input,0x201,0,point);SendMessage(input,0x202,0,point);
                await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
                Check(session.ExportAuthorityLog().EntryCount==step+1 && session.ExportAuthorityLog().Entries[^1].Submission.Accepted,"Native board click did not submit the expected action");Show();
            }
            Check(session.Ended,"Native graphical game did not end");
            stateHash=session.AuthorityStateHash();var recording=session.ExportRecording();recordingHash=Wire.Hash(RecordingCodec.Write(recording));
            Check(Wire.Json(recording.Replay(m=>board.Validate(content,m)).ExportAuthorityState())==Wire.Json(session.ExportAuthorityState()),"Graphical recording replay differs");
            var before=table.RenderedFrames;window.Width+=70;window.Height+=40;table.ChangeZoom(1.1f);await Until(()=>table.RenderedFrames>before+2,"Board resize stopped rendering");
            table.SaveImage(Output+".board.png");screenshots.Add(Output+".board.png");
            checks.Add("board-native-click-shuffle-draw-finish-replay-dpi-resize-zoom");
        }
        catch(Exception error){errors.Add(error.ToString());}
        finally
        {
            if(pump!=null)await pump.DisposeAsync();motionView?.Dispose();table?.Dispose();window.Content=null;
            if(motionView!=null)await motionView.Shutdown;if(table!=null)await table.Shutdown;if(host!=null)await host.DisposeAsync();
            window.Close();await Until(()=>PlatformViewport.LiveNativeWindows==0,"Native window leaked");
            if(Backend.LiveDevices!=0 || Backend.LiveBackends!=0)errors.Add("Graphics resources survived shutdown");
            Failed=errors.Count>0;
            File.WriteAllText(Output,JsonSerializer.Serialize(new Report(!Failed,errors,checks,screenshots,stateHash,recordingHash,Backend.LiveDevices,Backend.LiveBackends),ReportJson.Default.Report));
            desktop.Shutdown(Failed?1:0);
        }
    }
    [DllImport("user32.dll")]private static extern nint GetWindow(nint window,uint command);
    [DllImport("user32.dll")]private static extern nint SendMessage(nint window,uint message,nuint w,nint l);
    private const string State="""
{"format":1,"state":{"entities":{"deck":{"id":"deck","isContainer":true,"ordered":true,"capacity":3,"contents":["a","b","c"],"faces":["牌库"]},"hand":{"id":"hand","isContainer":true,"capacity":3,"owner":"p1","faces":["手牌"]},"a":{"id":"a","container":"deck","faces":["甲"],"faceResources":["marker"]},"b":{"id":"b","container":"deck","faces":["乙"],"faceResources":["marker"]},"c":{"id":"c","container":"deck","faces":["丙"],"faceResources":["marker"]}},"seats":[{"id":"p1","team":"one"}],"phases":["play"],"phase":"play"}}
""";
    private const string Startup="""
{"format":1,"id":"graphics-probe","version":"1","plugins":[{"id":"definition.board-state","version":"1","api":1,"configuration":{"path":"state.json"}},{"id":"resources","version":"1","api":1,"configuration":{}},{"id":"resources.images","version":"1","api":1,"configuration":{"sources":[{"path":"marker.png","name":"marker"}]}},{"id":"resources.font","version":"1","api":1,"configuration":{"path":"font.otf","license":"OFL.txt"}},{"id":"kernel","version":"1","api":1,"configuration":{}},{"id":"recording","version":"1","api":1,"configuration":{}},{"id":"session.local","version":"1","api":1,"configuration":{}},{"id":"rules.commands","version":"1","api":1,"configuration":{"actions":[{"id":"shuffle","label":"洗牌","command":{"kind":"shuffle","entity":"deck"}},{"id":"draw","label":"抽牌","command":{"kind":"draw","entity":"deck","target":"hand"}},{"id":"finish","label":"结束","command":{"kind":"finish","winners":["p1"]}}]}},{"id":"board.render","version":"1","api":1,"configuration":{}}]}
""";
}
internal sealed record Report(bool Success,List<string> Errors,List<string> Checks,List<string> Screenshots,string StateHash,string RecordingHash,int Devices,int Backends);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,WriteIndented=true)]
[JsonSerializable(typeof(Report))]
internal partial class ReportJson:JsonSerializerContext;
