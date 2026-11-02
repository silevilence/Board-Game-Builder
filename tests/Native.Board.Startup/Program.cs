using System.Text.Json;
using System.Diagnostics;
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
        var fingerprint=externalSelection.ValueKind==JsonValueKind.Undefined?null:externalSelection.GetProperty("implementation").GetString();
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
}
