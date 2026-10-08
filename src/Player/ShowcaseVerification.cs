using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Player;

internal static class ShowcaseVerification
{
    private static void Check(bool result,string message) {if(!result)throw new InvalidOperationException(message);}
    public static Window Create(string source,string output)
    {
        var window=new Window {Title="Showcase verification",Width=1500,Height=950};
        window.Opened+=async (_,_)=>
        {
            var results=new List<object>();IDisposable? active=null;
            try
            {
                var initial=GameRuntime.Load(source);
                var setup=new GameSetupWindow(initial,new(1,0,[initial.Initial.Seats[0].Id]),preset=>Task.Run(()=>GameRuntime.Load(source,preset)));
                var choosing=setup.ShowDialog<GameSetup?>(window);
                foreach(var preset in GamePresets.Choices(initial).Keys)
                {
                    setup.Preset.SelectedItem=preset;
                    for(var i=0;i<100 && GamePresets.Selected(setup.SelectedModule)!=preset;i++)await Task.Delay(30);
                    Check(GamePresets.Selected(setup.SelectedModule)==preset,"Setup preset did not load");
                    foreach(var human in setup.Humans.Values)human.IsChecked=false;
                    var last=setup.SelectedModule.Initial.Seats[^1].Id;setup.Humans[last].IsChecked=true;
                    Check(setup.TryRead(out var read) && read!.Humans.SequenceEqual(new[] {last}),"Last seat cannot be the only human");
                }
                setup.Mode.SelectedIndex=3;Check(setup.TryRead(out var cpuSetup) && cpuSetup!.Humans.Length==0,"All CPU setup failed");
                setup.Close();Check(await choosing==null,"Cancelled setup returned a new game");
                results.Add(new {settings="all presets, last human seat, all CPU, cancel"});
                var refs=initial.Initial.Entities.Values.SelectMany(e=>e.FaceResources).Distinct().ToArray();
                var atlas=new InteractiveTable {InputEnabled=false};active=atlas;window.Content=atlas;
                atlas.Show(new(refs.Select((r,i)=>new TableItem(null,"card","",new TableRect(10+i%13*86,10+i/13*126,80,110),r)).ToArray(),initial.Resources!));
                await Task.Delay(500);Check(atlas.RenderedFrames>0 && atlas.CachedImages==1,"Atlas did not share one GPU texture");
                atlas.SaveImage(output+".atlas.png");atlas.Dispose();window.Content=null;active=null;
                results.Add(new {atlas=true,faces=refs.Length,textures=1});
                foreach(var preset in GamePresets.Choices(initial).Keys)
                {
                    var module=GameRuntime.Load(source,preset);
                    var session=await PlayerSession.CreateAsync(module,1,"p2");var board=new PlayBoard(session,module);active=board;window.Content=board;
                    await board.PumpCpu();await Task.Delay(180);
                    board.SaveImage(output+"."+module.Initial.Seats.Count+"-players.png");
                    var renders=board.RenderedFrames;
                    for(var i=0;i<500 && !board.Frame.View.Ended && board.Frame.Fault==null;i++)
                    {
                        if(board.Frame.Cpu)await board.PumpCpu();
                        else await board.Act(new("action",Action:board.Frame.Actions[0].Id),board.Frame.Revision);
                    }
                    Check(board.Frame.View.Ended && board.Frame.Fault==null && renders>0,"Playable GUI did not render/finish");
                    results.Add(new {preset,finished=true,renders,textures=board.CachedImages});board.Dispose();active=null;window.Content=null;
                }
                foreach(var mode in Enum.GetValues<LocalTableMode>())
                {
                    var module=GameRuntime.Load(source,GamePresets.Choices(initial).Keys.Last());
                    var board=await LocalBoard.Create(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Id is "p2" or "p3"?"human":"cpu"),mode);active=board;window.Content=board;
                    await board.PumpCpu();var curtains=0;var renders=0;
                    for(var i=0;i<500 && !board.Frame.Ended && board.Frame.Fault==null;i++)
                    {
                        await Task.Delay(40);renders+=board.RenderedFrames;
                        if(board.Frame.WaitingFor!=null) {curtains++;Check(board.LiveTables==0,"Hotseat leaked prior table");}
                        await board.VerifyStep();
                    }
                    Check(board.Frame.Ended && board.Frame.Fault==null && renders>0,"Local GUI did not render/finish");
                    results.Add(new {mode=mode.ToString(),finished=true,curtains,renders});board.Dispose();active=null;window.Content=null;
                }
                Check(DesktopViewport.LiveDevices==0,"Graphics devices leaked");
                {
                    var module=GameRuntime.Load(source);
                    var board=await LocalBoard.Create(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"),LocalTableMode.HotSeat);active=board;window.Content=board;
                    var pumping=board.PumpCpu();var observations=0;
                    while(!pumping.IsCompleted)
                    {
                        var publicView=board.Frame.Views.Single().View;
                        Check(publicView.Seat==SeatView.AudienceSeat && !publicView.Entities.Any(e=>e.Container?.StartsWith("hand-")==true),"CPU spectator leaked a private hand");
                        observations++;await Task.Delay(60);
                    }
                    await pumping;Check(board.Frame.Ended && board.Frame.Fault==null && observations>0,"All CPU GUI did not complete");
                    board.Dispose();active=null;window.Content=null;results.Add(new {allCpu=true,observations});
                }
                Check(DesktopViewport.LiveDevices==0,"All CPU graphics devices leaked");
                File.WriteAllText(output,Wire.Json(new {success=true,results}));
            }
            catch(Exception e) {File.WriteAllText(output,Wire.Json(new {success=false,error=e.ToString(),results}));}
            finally {active?.Dispose();window.Close();}
        };
        return window;
    }
    internal sealed record NetworkProbe(string Module,string Preset,string Server,string RoomKey,string[] Seats);
    public static Window Online(string config,string output)
    {
        var window=new Window {Title="Online GUI verification",Width=450,Height=160};
        window.Opened+=async (_,_)=>
        {
            var clients=new List<OnlineWindow>();var sessions=new List<OnlinePlayerSession>();
            try
            {
                var probe=Wire.Read<NetworkProbe>(File.ReadAllText(config));var module=GameRuntime.Load(probe.Module,probe.Preset);
                foreach(var seat in probe.Seats.Append(SeatView.AudienceSeat))
                {
                    var session=new OnlinePlayerSession(module,new(probe.Server,probe.RoomKey,seat,seat==SeatView.AudienceSeat),output+"."+(seat==""?"audience":seat)+".identity.json");sessions.Add(session);
                    await session.ConnectAsync();var client=new OnlineWindow(module,session);clients.Add(client);client.Show();
                }
                var resumed=false;
                for(var i=0;i<1200;i++)
                {
                    foreach(var client in clients)await client.VerifyRefresh();
                    if(clients[0].Frame.View.Ended)break;
                    Check(clients.All(c=>c.Frame.Fault==null && !c.Frame.Closed),"Network GUI fault");
                    if(!resumed && clients[0].Frame.View.Phase=="play")
                    {await sessions[0].DisconnectAsync();await sessions[0].ConnectAsync();resumed=true;}
                    var active=clients.FirstOrDefault(c=>c.Frame.Actions.Count>0);
                    if(active!=null) await active.Act(new("action",Action:active.Frame.Actions[0].Id),active.Frame.Revision);
                    await Task.Delay(70);
                }
                Check(clients[0].Frame.View.Ended && resumed,"Graphical online session did not complete/recover");
                Check(clients.All(c=>c.RenderedFrames>0),"A network viewport did not render");
                var record=await sessions[0].RecordingAsync();var replay=new OnlineReplayWindow(module,record);replay.Show();await Task.Delay(250);replay.Close();
                File.WriteAllText(output,Wire.Json(new {success=true,clients=probe.Seats.Length,spectator=true,resumed,rendered=clients.Select(c=>c.RenderedFrames).ToArray(),replay=record.Replay().Frame!.View.Ended}));
            }
            catch(Exception e) {File.WriteAllText(output,Wire.Json(new {success=false,error=e.ToString()}));}
            finally {foreach(var client in clients)client.Close();foreach(var session in sessions)session.Dispose();window.Close();}
        };
        return window;
    }
}
