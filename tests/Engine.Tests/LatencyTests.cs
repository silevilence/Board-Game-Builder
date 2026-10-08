using System.Diagnostics;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;

public static class LatencyTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var module=TestModules.Load("samples/quick-cards");
        var calls=new List<(string Path,string Entry,double Ms)>();
        using var listener=new ActivityListener
        {
            ShouldListenTo=source=>source.Name=="BoardGame.Lua",
            Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,
            ActivityStopped=call=> { lock(calls) calls.Add(((string)call.GetTagItem("lua.path")!,(string)call.GetTagItem("lua.entry")!,call.Duration.TotalMilliseconds)); }
        };
        ActivitySource.AddActivityListener(listener);
        var samples=new List<double>(); string? previous=null;
        for(var game=0;game<2;game++)
        {
            using var session=await PlayerSession.CreateAsync(module,42,"p1");
            try
            {
                while(!session.Frame.View.Ended && session.Frame.Revision<10)
                {
                    Check.That(session.Frame.Fault==null && !session.Frame.Cpu);
                    var clock=Stopwatch.StartNew();
                    Check.That((await session.SubmitAsync(session.Frame.Revision,new("action",Action:session.Frame.Actions[0].Id))).Accepted);
                    while(session.Frame.Cpu && !session.Frame.View.Ended && session.Frame.Fault==null)
                        Check.That(await session.StepCpuAsync());
                    samples.Add(clock.Elapsed.TotalMilliseconds);
                }
                Check.That(session.Frame.View.Ended && session.Frame.Fault==null);
                var state=Wire.Json(session.Frame.View);
                if(previous!=null) Check.That(previous==state,"Restart changed deterministic outcome");
                previous=state;
            }
            finally { session.Stop(); }
        }
        Console.WriteLine("Human + CPU latency ms: "+string.Join(", ",samples.Select(x=>x.ToString("F1",System.Globalization.CultureInfo.InvariantCulture))));
        foreach(var group in calls.GroupBy(c=>(Path.GetFileName(c.Path),c.Entry)))
            Console.WriteLine($"Lua {group.Key}: count={group.Count()} mean={group.Average(c=>c.Ms):F1} ms max={group.Max(c=>c.Ms):F1} ms (includes IPC/startup)");
        Check.That(samples.Max()<1000,"Simple card interaction exceeded the 1000 ms local performance budget");
    }
}
