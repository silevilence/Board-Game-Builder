using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;
using BoardGame.View;
using BoardGame.Kernel;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args is ["prepare-seats",var destination]){PrepareSeats(Path.GetFullPath(destination));return 0;}
        var source=Path.GetFullPath(args[0]);var report=Path.GetFullPath(args[1]);
        var starts=args.Contains("--once")?1:5;
        using var process=Process.GetCurrentProcess();
        var startup=(DateTime.UtcNow-process.StartTime.ToUniversalTime()).TotalMilliseconds;
        var elapsed=Stopwatch.StartNew();
        var metrics=report+".csv";
        File.WriteAllText(metrics,"phase,elapsedMs,workingSet,privateBytes\n");
        void Measure(string name)
        {
            process.Refresh();
            File.AppendAllText(metrics,$"{name},{startup+elapsed.Elapsed.TotalMilliseconds:F0},{process.WorkingSet64},{process.PrivateMemorySize64}\n");
        }
        var phase="content load";
        using var watchdog=new CancellationTokenSource();
        _=Task.Run(async()=>
        {
            try{await Task.Delay(TimeSpan.FromSeconds(120),watchdog.Token);File.WriteAllText(report,"FAIL timeout: "+phase);Environment.Exit(2);}
            catch(OperationCanceledException){}
        });
        using var entry=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"game.start.json")));
        var externalSelection=entry.RootElement.GetProperty("plugins").EnumerateArray().SingleOrDefault(p=>p.GetProperty("id").GetString()=="sample.native-cpu");
        var fingerprint=externalSelection.ValueKind==JsonValueKind.Undefined || !externalSelection.TryGetProperty("implementation",out var implementation)?null:implementation.GetString();
        var external=Sample.NativeCpu.Plugin.Create();
        var catalog=new ApplicationCatalog([
            BoardStaticFactories.Definition(),BoardStaticFactories.Authority(),BoardStaticFactories.Rules(),BoardStaticFactories.Recording(),
            BoardStaticFactories.Session(),BoardStaticFactories.CpuSession(),BoardStaticFactories.EffectsSession(),BoardStaticFactories.CpuEffectsSession(),
            AssetStaticFactories.Resources(),AssetStaticFactories.Images(),BoardRenderPlugin.Renderer(),BoardRenderPlugin.VisualInput(),ViewportTargetPlugin.Create(),
            DesktopGlBackend.Create(),WasmStaticFactories.Create(),new(content=>external.Bind(content) with {Implementation=fingerprint})]);
        var content=catalog.Load(source);string? error=null;phase="first window open";Measure("content");
        NativeBoardWindow? tested=null;string? recordingHash=null;
        var result=NativeApplication.Run(()=>
        {
            var window=new NativeBoardWindow(catalog,content,NativeApplication.Graphics(catalog,content),"Native startup regression");
            tested=window;
            window.Opened+=async(_,_)=>
            {
                try
                {
                    if(args.Length>2 && args[2] is "sync" or "async")
                        for(var attempt=0;attempt<20;attempt++)
                        {
                            phase=args[2]+" preparation "+attempt;
                            if(args[2]=="sync")_=catalog.Load(source);
                            else _=await Task.Run(()=>catalog.Load(source));
                            Measure("prepare-"+attempt);
                        }
                    for(var repeat=0;repeat<starts;repeat++)
                    {
                        phase="ready "+repeat;
                        await window.Ready;
                        Measure("ready-"+repeat);
                        if(args.Contains("--seats"))await VerifySeats(window);
                        phase="render "+repeat;
                        for(var wait=0;window.Table?.RenderedFrames<3 && wait<100;wait++)await Task.Delay(20);
                        if(window.Error!=null || window.Table is not {RenderedFrames:>=3})throw new InvalidOperationException(window.Error ?? "No rendered frames");
                        Measure("frame-"+repeat);
                        if(args.Contains("--play"))
                        {
                            phase="play "+repeat;
                            await window.Pump!.SetPausedAsync(true);
                            var tick=window.Instance!.Driver.CompletedTick;await Task.Delay(120);
                            if(window.Instance.Driver.CompletedTick!=tick)throw new InvalidOperationException("Render advanced update");
                            if(window.Table.CurrentScene.Items.Any(p=>p.Entity=="private"))throw new InvalidOperationException("Private entity leaked");
                            var session=window.Session!;
                            var ids=new List<string>{"shuffle","draw"};
                            if(content.Identity.Startup.Plugins.Any(p=>p.Id=="wasm"))ids.Add("add");
                            ids.Add(session.SupportsCpu?"next":"end");
                            foreach(var id in ids)
                            {
                                window.Submit(session.Actions("p1").Single(p=>p.Id==id).Command);await window.Pump.StepAsync();
                                if(!session.ExportAuthorityLog().Entries[^1].Submission.Accepted)throw new InvalidOperationException("Rejected "+id);
                            }
                            if(session.SupportsCpu)await window.Pump.StepAsync();
                            if(!session.Ended || session.ExportAuthorityState().Entities["hand"].Contents.Count!=1)throw new InvalidOperationException("Game did not complete");
                            if(ids.Contains("add") && session.ExportAuthorityState().Entities["counter"].Properties["wasm.counter"]!=2)throw new InvalidOperationException("WASM result differs");
                            var recording=RecordingCodec.Write(session.ExportRecording());window.ValidateRecording(recording);
                            var hash=Wire.Hash(recording);
                            if(recordingHash!=null && recordingHash!=hash)throw new InvalidOperationException("Restart changed fixed seed recording");
                            recordingHash=hash;File.WriteAllText(report+".recording.json",recording);Measure("played-"+repeat);
                            var saving=window.SaveRecording();
                            if(!ReferenceEquals(window.Ready,saving))throw new InvalidOperationException("Recording save is not owned by the window");
                            if(repeat+1==starts){phase="close while recording";window.Close();}
                            await saving;
                        }
                        if(repeat+1<starts){phase="restart "+repeat;await window.Restart();}
                    }
                    if(args.Contains("--seats") && window.Session!.ExportAuthorityState().Seats.Count(s=>s.Controller=="human")>1)
                    {
                        await window.Restart();
                        Check(window.WaitingFor=="p2" && window.Table==null,"Restart exposed the next player's board before confirmation");
                        phase="close while awaiting handoff";
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{phase="close";window.Close();}
            };
            return window;
        });
        watchdog.Cancel();
        Measure("closed");
        error ??=tested?.Error;
        if(PlatformViewport.LiveNativeWindows!=0)error=$"Native viewports leaked: {PlatformViewport.LiveNativeWindows}; instance={tested?.Instance!=null}; table={tested?.Table!=null}; prior={error}";
        File.WriteAllText(report,error==null?$"PASS {starts} starts, rendered frames, awaited close"+(recordingHash==null?"":"; gameplay/recording="+recordingHash):error);
        return error==null?result:1;
    }
    private static async Task VerifySeats(NativeBoardWindow window)
    {
        Check(window.Error==null,window.Error ?? "Window failed");
        await window.Pump!.SetPausedAsync(true);
        var session=window.Session!;
        var humans=session.ExportAuthorityState().Seats.Where(s=>s.Controller=="human").ToArray();
        if(humans.Length>1)
        {
            Check(session.CurrentSeat=="p2","Fixture must start at second human");
            await Take("p2","p1");
            window.Submit(new(){Kind="next"});await window.Pump.StepAsync();window.Refresh();
            Check(session.CurrentSeat=="p1","Turn did not reach first human");
            await Take("p1","p2");
            window.Submit(new(){Kind="finish",Winners=["p1"]});await window.Pump.StepAsync();window.Refresh();
            Check(session.Ended,"Second human could not complete game");
        }
        else
        {
            Check(window.WaitingFor==null && window.Table!=null,"Solo/spectator unexpectedly requires handoff");
            window.Refresh();await Frames();
            if(humans.Length==0)
            {
                Check(window.VisibleSeat==SeatView.AudienceSeat,"All CPU view is not an audience projection");
                Check(!window.Table!.CurrentScene.Items.Any(p=>p.Entity is "private-p1" or "private-p2"),"CPU owner-only entity leaked");
                var before=session.ExportAuthorityLog().EntryCount;window.Submit(new(){Kind="next"});
                await window.Pump.StepAsync();
                Check(session.ExportAuthorityLog().Entries.Skip(before).All(e=>e.Submission.Execution!="player"),"Spectator submitted a human command");
            }
            else
            {
                Check(window.VisibleSeat=="p1","Single human seat changed");CheckPrivate("p1","p2");
                window.Submit(new(){Kind="next"});await window.Pump.StepAsync();await window.Pump.StepAsync();window.Refresh();
                Check(session.Ended && window.WaitingFor==null,"Single human plus CPU no longer completes");
            }
        }
        window.ValidateRecording(RecordingCodec.Write(session.ExportRecording()));
        async Task Take(string expected,string hidden)
        {
            Check(window.WaitingFor==expected && window.VisibleSeat==null && window.Table==null,"Private board visible before confirmation for "+expected);
            var before=session.ExportAuthorityLog().EntryCount;
            window.Submit(new(){Kind="finish",Winners=[expected]});await window.Pump.StepAsync();
            Check(session.ExportAuthorityLog().EntryCount==before,"Command accepted while waiting for handoff");
            await window.ConfirmHandoff();await Frames();
            Check(window.WaitingFor==null && window.VisibleSeat==expected,"Confirmed seat was not selected");CheckPrivate(expected,hidden);
        }
        async Task Frames()
        {
            for(var wait=0;window.Table?.RenderedFrames<3 && wait<100;wait++)await Task.Delay(20);
            Check(window.Error==null && window.Table is {RenderedFrames:>=3},window.Error ?? "No rendered seat frames");
        }
        void CheckPrivate(string visible,string hidden)
        {
            Check(window.Table!.CurrentScene.Items.Any(p=>p.Entity=="private-"+visible),"Owner's entity missing");
            Check(!window.Table.CurrentScene.Items.Any(p=>p.Entity=="private-"+hidden),"Previous/other seat private entity leaked");
        }
    }
    private static void PrepareSeats(string destination)
    {
        if(Directory.Exists(destination))throw new IOException("Seat fixtures require a new destination");
        Directory.CreateDirectory(destination);
        foreach(var mode in new[]{"hotseat","single","spectator"})
        {
            var directory=Path.Combine(destination,mode);Directory.CreateDirectory(directory);
            var state=new GameState{Seats=[new("p1","one",mode=="spectator"?"cpu":"human"),new("p2","two",mode=="hotseat"?"human":"cpu")],
                CurrentSeat=mode=="hotseat"?1:0,Phases=["play"],Phase="play",Entities=new(StringComparer.Ordinal){
                    ["public"]=new(){Id="public",Faces=["public"],FaceUp=true},
                    ["private-p1"]=new(){Id="private-p1",Owner="p1",Visibility="owner",Faces=["secret one"],X=150},
                    ["private-p2"]=new(){Id="private-p2",Owner="p2",Visibility="owner",Faces=["secret two"],X=300}}};
            File.WriteAllText(Path.Combine(directory,"state.json"),new JsonObject{["format"]=1,["state"]=JsonNode.Parse(Wire.Json(state))}.ToJsonString());
            RuntimePluginSelection P(string id,string config="{}")
            {using var json=JsonDocument.Parse(config);return new(id,"1",1,json.RootElement.Clone());}
            var selections=new List<RuntimePluginSelection>{P("definition.board-state","""{"path":"state.json"}"""),P("resources"),P("kernel"),P("recording"),
                P(mode=="hotseat"?"session.local":"session.local.cpu"),P("rules.commands","""{"actions":[{"id":"next","label":"Next","command":{"kind":"next"}},{"id":"end","label":"End","command":{"kind":"finish","winners":["p1"]}}]}"""),
                P("render.desktopgl"),P("platform.viewport"),P("board.visual-input"),P("board.render")};
            if(mode!="hotseat")selections.Add(P("sample.native-cpu","""{"action":"end"}"""));
            File.WriteAllText(Path.Combine(directory,"game.start.json"),Wire.Json(new RuntimeStartup(1,"seat-regression-"+mode,"1",selections)));
        }
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
