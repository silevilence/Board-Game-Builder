using System.Diagnostics;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class SessionDriverTests
{
    public static void Run()=>Verify().GetAwaiter().GetResult();
    private static async Task Verify()
    {
        var module=GameRuntime.Load("samples/quick-cards");
        using var first=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        using var second=new PlaySession(module,7,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        first.Driver.Paused=true;
        await first.AdvanceAsync(TimeSpan.FromSeconds(1));
        Check.That(first.Steps==0 && first.Driver.CompletedTick==0);
        Check.That(await first.StepCpuAsync() && first.Steps==1 && first.Driver.CompletedTick==1);
        Check.That(second.Steps==0 && second.Driver.CompletedTick==0,"Hosts shared clocks or state");
        await second.StepCpuAsync();Check.That(second.Steps==1 && first.Steps==1);
        using var human=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"));
        var seat=human.CurrentSeat;var action=human.Actions().First().Command;
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();
            try {await human.SubmitHumanAsync(seat,action,canceled.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException) { }
        }
        await human.AdvanceAsync(TimeSpan.FromSeconds(.01));Check.That(human.Steps==0,"Cancelled input survived in queue");
        var submitted=await human.SubmitHumanAsync(seat,action);
        Check.That(submitted.Accepted && submitted.Sequence==1 && human.Driver.CompletedTick==2);

        using var target=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        using var cancellation=new CancellationTokenSource();var armed=true;var calls=0;
        using var listener=new ActivityListener
        {
            ShouldListenTo=source=>source.Name=="BoardGame.Lua",
            Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,
            ActivityStarted=_=>{calls++;if(armed)cancellation.Cancel();}
        };
        ActivitySource.AddActivityListener(listener);
        try {await target.StepCpuAsync(cancellation.Token);throw new Exception("Lua invocation ignored cancellation");}
        catch(OperationCanceledException) { }
        Check.That(calls>0 && target.Steps==0 && target.Fault==null && target.ExportAuthorityLog().EntryCount==0);
        armed=false;Check.That(await target.StepCpuAsync() && target.Steps==1,"Invocation cancellation leaked into the next tick");
        await target.DisposeAsync();await target.DisposeAsync();
    }
}
