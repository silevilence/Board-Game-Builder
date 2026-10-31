using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;
using BoardGame.View;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        var source=Path.GetFullPath(args[0]);var report=Path.GetFullPath(args[1]);
        var phase="content load";
        using var watchdog=new CancellationTokenSource();
        _=Task.Run(async()=>
        {
            try{await Task.Delay(TimeSpan.FromSeconds(45),watchdog.Token);File.WriteAllText(report,"FAIL timeout: "+phase);Environment.Exit(2);}
            catch(OperationCanceledException){}
        });
        using var entry=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"game.start.json")));
        var fingerprint=entry.RootElement.GetProperty("plugins").EnumerateArray().Single(p=>p.GetProperty("id").GetString()=="sample.native-cpu").GetProperty("implementation").GetString();
        var external=Sample.NativeCpu.Plugin.Create();
        var catalog=new ApplicationCatalog([
            BoardStaticFactories.Definition(),BoardStaticFactories.Authority(),BoardStaticFactories.Rules(),BoardStaticFactories.Recording(),BoardStaticFactories.CpuEffectsSession(),
            AssetStaticFactories.Resources(),AssetStaticFactories.Images(),BoardRenderPlugin.Renderer(),BoardRenderPlugin.VisualInput(),ViewportTargetPlugin.Create(),
            DesktopGlBackend.Create(),WasmStaticFactories.Create(),new(content=>external.Bind(content) with {Implementation=fingerprint})]);
        var content=catalog.Load(source);string? error=null;phase="first window open";
        var result=NativeApplication.Run(()=>
        {
            var window=new NativeBoardWindow(catalog,content,NativeApplication.Graphics(catalog,content),"Native startup regression");
            window.Opened+=async(_,_)=>
            {
                try
                {
                    for(var repeat=0;repeat<5;repeat++)
                    {
                        phase="ready "+repeat;
                        await window.Ready;
                        phase="render "+repeat;
                        for(var wait=0;window.Table?.RenderedFrames<3 && wait<100;wait++)await Task.Delay(20);
                        if(window.Error!=null || window.Table is not {RenderedFrames:>=3})throw new InvalidOperationException(window.Error ?? "No rendered frames");
                        if(repeat<4){phase="restart "+repeat;await window.Restart();}
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{phase="close";window.Close();}
            };
            return window;
        });
        watchdog.Cancel();
        if(PlatformViewport.LiveNativeWindows!=0)error="Native viewports leaked";
        File.WriteAllText(report,error==null?"PASS five starts, rendered frames, awaited close":error);
        return error==null?result:1;
    }
}
