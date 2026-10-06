using Avalonia.Controls;
using Avalonia.Threading;
using BoardGame.View;
using CordiSharp;
using CordiSharp.Plugins.Avalonia;
using Avalonia.Controls.Templates;

internal static class Program
{
    [STAThread] static int Main(string[] args) => ViewportApp.Run("",[],()=>
    {
        var tabs=new TabControl();
        Slot.SetId(tabs,"compatibility.panels");
        var window=new Window {Title="CordiSharp compatibility",Width=900,Height=650,Content=tabs};
        window.Opened+=async (_,_)=>
        {
            var failures=new List<string>();
            var root=Context.Create();
            var draws=0;
            try
            {
                await root.Plugin(typeof(AvaloniaUIService));
                for(var cycle=0;cycle<3;cycle++)
                {
                    var provider=root.Inject([], (ctx,_)=>
                    {
                        ctx.Provide("probe",new object());
                        Panel(ctx,"Provider",()=>new TextBlock {Text="Avalonia 12.1.1 + CordiSharp 0.2.0"});
                        return null;
                    });
                    await provider;
                    DesktopViewport? viewport=null;
                    var dependent=root.Inject(["probe"],(ctx,_)=>
                    {
                        Panel(ctx,"Dependent viewport",()=>viewport=new DesktopViewport());
                        return null;
                    });
                    await dependent;
                    tabs.SelectedIndex=1;
                    window.Width+=30;
                    await Task.Delay(350);
                    viewport!.ChangeZoom(1.1f);
                    if(viewport.Frames==0 || viewport.Error!=null) throw new Exception(viewport.Error ?? "No frames");
                    draws+=viewport.Frames;
                    await dependent.DisposeAsync();
                    await provider.DisposeAsync();
                    if(tabs.Items.Count!=0 || DesktopViewport.LiveDevices!=0) throw new Exception("UI/device leak");
                    var broken=root.Plugin((Action<Context>)(ctx=>
                    {
                        Panel(ctx,"failed",()=>new TextBlock());
                        throw new InvalidOperationException("intentional failure");
                    }));
                    try {await broken; throw new Exception("Failure not reported");}
                    catch(InvalidOperationException e) when(e.Message.Contains("intentional")) { }
                    await broken.DisposeAsync();
                    if(tabs.Items.Count!=0) throw new Exception("Failed panel survived");
                }
                var pending=root.Inject(["missing"],(_,_)=>throw new Exception("Must not activate"));
                await Task.Delay(50);
                if(pending.State!=FiberState.Pending) throw new Exception("Missing dependency did not wait");
                await pending.DisposeAsync();
            }
            catch(Exception e) { failures.Add(e.ToString()); }
            finally
            {
                await root.Fiber.DisposePluginAsync();
                File.WriteAllText(Path.GetFullPath(args[0]),System.Text.Json.JsonSerializer.Serialize(new {cycles=3,draws,devices=DesktopViewport.LiveDevices,failures}));
                window.Close();
            }
            void Panel(Context ctx,string title,Func<Control> factory)
            {
                var ui=root.Get<AvaloniaUIService>("avalonia")!;
                // Evaluate user factories before registering with the upstream slot service.
                var tab=ui.OnUi(()=>new TabItem {Header=title,Content=factory()});
                ctx.Effect(()=> (Action)(()=>ui.OnUi(()=> {(tab.Content as IDisposable)?.Dispose();tab.Content=null;})));
                ui.RegisterSlot(ctx,"compatibility.panels",new FuncDataTemplate<object>((_,_)=>null),()=>new[]{tab});
            }
        };
        return window;
    });
}
