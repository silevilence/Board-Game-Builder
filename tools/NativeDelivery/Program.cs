using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Player;
using BoardGame.Plugins;
using BoardGame.Publishing;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;
using BoardGame.View;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length==2 && args[0]=="prepare"){Prepare(Path.GetFullPath(args[1]));return 0;}
        if(args.Length==4 && args[0]=="build")return Build(args[1],args[2],args[3]).GetAwaiter().GetResult();
        if(args.Length==3 && args[0]=="verify-ui")return NativeApplication.Run(()=>Verify(args[1],args[2]));
        if(args.Length==3 && args[0]=="compare-recordings")
        {
            var left=RecordingCodec.Read(File.ReadAllText(args[1])).Replay(_=>{}).ExportAuthorityState();
            var right=RecordingCodec.Read(File.ReadAllText(args[2])).Replay(_=>{}).ExportAuthorityState();
            Check(Wire.Json(left)==Wire.Json(right),"Native and ordinary authority state differ");Console.WriteLine("PASS state="+Wire.Hash(Wire.Json(left)));return 0;
        }
        throw new ArgumentException("prepare <new-root> | build <sdk> <source> <new-output> | verify-ui <source> <new-report-directory>");
    }
    private static ApprovedApplicationPlugin[] Builtins()=>[
        BoardStaticFactories.Definition(),BoardStaticFactories.Authority(),BoardStaticFactories.Rules(),BoardStaticFactories.Recording(),
        BoardStaticFactories.Session(),BoardStaticFactories.CpuSession(),BoardStaticFactories.EffectsSession(),BoardStaticFactories.CpuEffectsSession(),
        AssetStaticFactories.Resources(),AssetStaticFactories.Images(),AssetStaticFactories.Font(),BoardRenderPlugin.Renderer(),BoardRenderPlugin.VisualInput(),
        ViewportTargetPlugin.Create(),DesktopGlBackend.Create(),WasmStaticFactories.Create()];
    private static PluginStore Store(string root)=>new(Path.Combine(root,"installed"),"win-x64",[
        typeof(GameState).Assembly,typeof(ICpuDecisionProvider).Assembly,typeof(IGameSession).Assembly,typeof(IApplicationView).Assembly,
        typeof(IDraw2D).Assembly,typeof(AssetSet).Assembly]);
    private static (GameContent Content,PluginCatalogLease Lease) Open(string source)
    {
        var root=Path.GetDirectoryName(Path.GetFullPath(source))!;var store=Store(root);
        var startup=Wire.Read<RuntimeStartup>(File.ReadAllText(Path.Combine(source,GameRuntime.Entry)));
        var lease=store.Open(startup,new ApplicationCatalog(Builtins()));
        try{return(lease.Catalog.Load(source),lease);}catch{lease.Dispose();throw;}
    }
    private static void Prepare(string root)
    {
        if(Directory.Exists(root))throw new IOException("Fixture already exists");Directory.CreateDirectory(root);
        var package=Path.Combine(root,"cpu-package");Directory.CreateDirectory(package);
        var assembly=typeof(Sample.NativeCpu.Plugin).Assembly.Location;File.Copy(assembly,Path.Combine(package,Path.GetFileName(assembly)));
        var definition=Sample.NativeCpu.Plugin.Create().Bind(null);
        var manifest=new PluginManifest(1,"runtime",definition.Id,"1",1,new(Path.GetFileName(assembly),typeof(Sample.NativeCpu.Plugin).FullName!,"Create"),["win-x64"],
            new(true,["win-x64"],"Validated by NativeDelivery actual native publication and clean Windows execution"),[new("cpu",1)],[],definition.Execution,
            [new(Path.GetFileName(assembly),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly))),"implementation")]) {ContentFields=["action"]};
        File.WriteAllText(Path.Combine(package,PluginManifest.FileName),manifest.Write());Store(root).Import(package);
        foreach(var (name,wasm,cpu) in new[]{("minimal",false,false),("wasm",true,false),("external",false,true),("wasm-external",true,true)})
        {
            var folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
            var state=new GameState{Seats=cpu?[new("p1","真人"),new("p2","CPU","cpu")]:[new("p1","真人")],Phases=["play"],Phase="play",
                Entities=new(StringComparer.Ordinal){
                    ["deck"]=new(){Id="deck",IsContainer=true,Ordered=true,Capacity=3,Contents=["A","a","b"]},
                    ["hand"]=new(){Id="hand",IsContainer=true,Owner="p1",Capacity=3},
                    ["A"]=new(){Id="A",Container="deck",Faces=["中文甲"],FaceResources=["card"]},
                    ["a"]=new(){Id="a",Container="deck",Faces=["中文乙"],FaceResources=["card"]},
                    ["b"]=new(){Id="b",Container="deck",Faces=["中文丙"],FaceResources=["card"]},
                    ["counter"]=new(){Id="counter",Properties=new(StringComparer.Ordinal){["wasm.counter"]=0}},
                    ["private"]=new(){Id="private",Owner=cpu?"p2":null,Visibility="hidden",Faces=["不应可见"]}
                }};
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonSerializer.Serialize(new{format=1,state},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            File.Copy("samples/freecell/assets/card_clubs_A.png",Path.Combine(folder,"card.png"));
            RuntimePluginSelection P(string id,object? config=null)=>new(id,"1",1,JsonSerializer.SerializeToElement(config ?? new{}));
            var offers=new List<ActionOffer>{new("shuffle","洗牌",new(){Kind="shuffle",Entity="deck"}),new("draw","抽牌",new(){Kind="draw",Entity="deck",Target="hand"}),new("end","结束",new(){Kind="finish",Winners=["p1"]})};
            if(wasm)offers.Add(new("add","WASM 加二",new(){Kind="wasm.counter.command",Entity="counter",Value=2}));
            if(cpu)offers.Add(new("next","交给 CPU",new(){Kind="next"}));
            var selections=new List<RuntimePluginSelection>{P("definition.board-state",new{path="state.json"}),P("resources"),P("resources.images",new{sources=new[]{new{path="card.png",name="card"}}}),
                P("kernel",new{seed=42}),P(cpu?wasm?"session.local.cpu-effects":"session.local.cpu":wasm?"session.local.effects":"session.local"),P("recording"),
                P("rules.commands",JsonSerializer.SerializeToElement(new{actions=offers},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),
                P("render.desktopgl"),P("platform.viewport"),P("board.visual-input"),P("board.render")};
            if(cpu)selections.Add(P(manifest.Id,new{action="end"}) with {Implementation=manifest.Fingerprint});
            if(wasm){File.Copy("samples/wasm-counter/counter.wasm",Path.Combine(folder,"counter.wasm"));selections.Add(Wire.Read<RuntimeStartup>(File.ReadAllText("samples/wasm-counter/game.start.json")).Plugins.Single(p=>p.Id=="wasm"));}
            File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(new RuntimeStartup(1,"原生桌游-"+name,"1",selections)));
            var opened=Open(folder);using(opened.Lease)Console.WriteLine(name+": "+opened.Content.Fingerprint);
        }
        Console.WriteLine(root);
    }
    private static async Task<int> Build(string sdkPath,string source,string output)
    {
        var opened=Open(source);using var lease=opened.Lease;var sdk=RuntimeSdk.Open(sdkPath);
        var plan=sdk.Plan(opened.Content,"board.desktop",[],lease.Inputs.Select(p=>new AvailablePlugin(p)));
        var result=await NativeBuildService.Build(plan,sdk.Entry("board.desktop",opened.Content.Identity.Startup.Id,"1"),new(output),new Progress<NativeBuildProgress>(p=>Console.WriteLine(p.Stage+": "+p.Message)));
        Console.WriteLine(JsonSerializer.Serialize(result));return result.Success?0:1;
    }
    private static Window Verify(string source,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);var opened=Open(source);var lease=opened.Lease;
        var window=new NativeBoardWindow(lease.Catalog,opened.Content,NativeApplication.Graphics(lease.Catalog,opened.Content),"桌游发行验收");
        var errors=new List<string>();var hashes=new List<string>();
        var repetitions=int.TryParse(Environment.GetEnvironmentVariable("BGB_VERIFY_STARTS"),out var configured)?configured:2;
        var watchdog=new CancellationTokenSource();
        _=Task.Run(async()=>
        {
            try{await Task.Delay(TimeSpan.FromSeconds(60),watchdog.Token);File.WriteAllText(Path.Combine(output,"timeout.txt"),"NativeBoardWindow did not complete startup/restart/close");Environment.Exit(2);}
            catch(OperationCanceledException){}
        });
        window.Opened+=async(_,_)=>
        {
            try
            {
                for(var repeat=0;repeat<repetitions;repeat++)
                {
                    await window.Ready;Check(window.Error==null,window.Error ?? "");await window.Pump!.SetPausedAsync(true);
                    for(var wait=0;window.Table!.RenderedFrames<3 && wait<100;wait++)await Task.Delay(30);
                    Check(window.Table.RenderedFrames>=3,window.Table.Surface.Error ?? "No rendered frames");
                    var tick=window.Instance!.Driver.CompletedTick;await Task.Delay(120);Check(window.Instance.Driver.CompletedTick==tick,"Render advanced update");
                    Check(!window.Table.CurrentScene.Items.Any(p=>p.Entity=="private"),"Private entity leaked");
                    var session=window.Session!;
                    var ids=new List<string>{"shuffle","draw"};if(opened.Content.Identity.Startup.Plugins.Any(p=>p.Id=="wasm"))ids.Add("add");
                    var cpu=session.SupportsCpu;ids.Add(cpu?"next":"end");
                    foreach(var id in ids)
                    {
                        window.Submit(session.Actions("p1").Single(p=>p.Id==id).Command);await window.Pump.StepAsync();
                        Check(session.ExportAuthorityLog().Entries[^1].Submission.Accepted,id+": "+session.Fault?.Detail);
                    }
                    if(cpu)await window.Pump.StepAsync();Check(session.Ended,"Game did not end: "+session.Fault?.Detail);
                    window.Refresh();var text=RecordingCodec.Write(session.ExportRecording());window.ValidateRecording(text);hashes.Add(Wire.Hash(text));
                    Check(session.ExportAuthorityState().Entities["hand"].Contents.Count==1,"Draw missing");
                    if(ids.Contains("add"))Check(session.ExportAuthorityState().Entities["counter"].Properties["wasm.counter"]==2,"WASM effect differs");
                    File.WriteAllText(Path.Combine(output,$"recording-{repeat}.json"),text);
                    window.Table.SaveImage(Path.Combine(output,$"board-{repeat}.png"));
                    if(repeat+1<repetitions)await window.Restart();
                }
                Check(hashes[0]==hashes[1],"Restart changed fixed seed recording");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally{window.Close();}
        };
        window.Closed+=(_,_)=>
        {
            watchdog.Cancel();watchdog.Dispose();
            lease.Dispose();if(PlatformViewport.LiveNativeWindows!=0)errors.Add("Native viewports leaked");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,hashes,errors}));
        };
        return window;
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
}
