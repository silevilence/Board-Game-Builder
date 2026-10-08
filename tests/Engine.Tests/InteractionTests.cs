using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class InteractionTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var module=TestModules.Load("samples/quick-cards"); using var ui=await PlayerSession.CreateAsync(module,42,"p1");
        var start=ui.SubmitAsync(0,new("action",Action:"start")); var duplicate=await ui.SubmitAsync(0,new("action",Action:"start"));
        Check.That(!duplicate.Accepted); Check.That((await start).Accepted);
        var old=ui.Frame; var card=old.Actions[0].Command.Entity;
        var result=await ui.SubmitAsync(old.Revision,new("drag",card,"deck")); Check.That(!result.Accepted);
        Check.That(BoardGame.Kernel.Wire.Json(old.View)==BoardGame.Kernel.Wire.Json(ui.Frame.View));
        Check.That(!(await ui.SubmitAsync(old.Revision,new("drag",card,"table"))).Accepted);
        Check.That((await ui.SubmitAsync(ui.Frame.Revision,new("drag",card,"table"))).Accepted);
        using var cli=new PlaySession(module,42); cli.SubmitHuman("p1",cli.Actions()[0].Command); cli.SubmitHuman("p1",cli.Actions()[0].Command);
        Check.That(BoardGame.Kernel.Wire.Json(cli.View("p1"))==BoardGame.Kernel.Wire.Json(ui.Frame.View));
        Check.That(ui.Frame.Cpu && ui.Frame.Actions.Count==0); Check.That(await ui.StepCpuAsync());
        using var p2=await PlayerSession.CreateAsync(module,42,"p2"); Check.That(p2.Frame.View.Seat=="p2" && p2.Frame.Actions.Count==0);
        using var broken=await PlayerSession.CreateAsync(module with {Cpu="return {decide=function(ctx) error('PRIVATE_CPU_CARD') end}"},42,"p2");
        Check.That(!await broken.StepCpuAsync() && broken.Frame.Fault?.Code=="cpu-failure");
        Check.That(!BoardGame.Kernel.Wire.Json(broken.Frame).Contains("PRIVATE_CPU_CARD"),"CPU diagnostic leaked to player");
        using var noActions=await PlayerSession.CreateAsync(module with {Rules="return {actions=function(ctx) return {} end}"},42,"p1");
        Check.That(noActions.Frame.Fault?.Code=="no-actions");
        using var fresh=await PlayerSession.CreateAsync(module,42,"p1"); Check.That(fresh.Frame.Revision==0 && fresh.Frame.View.Phase=="setup");
        const string slowRules="return {actions=function(c) return {{id='wait',label='Wait',command={kind='wait'}}} end,decide=function(c) local s=string.rep('a',1000000); while true do s=string.reverse(s) end end}";
        using var stopped=await PlayerSession.CreateAsync(module with {Rules=slowRules},42,"p1");
        var pending=stopped.SubmitAsync(0,new("action",Action:"wait"));
        await Task.Delay(100);stopped.Stop();
        Check.That(!(await pending.WaitAsync(TimeSpan.FromSeconds(2))).Accepted && !stopped.Busy);
        Check.That(stopped.Frame.Revision==0,"Stopped request published a new frame");
        Check.That(!(await stopped.SubmitAsync(0,new("action",Action:"wait"))).Accepted);
    }
}
