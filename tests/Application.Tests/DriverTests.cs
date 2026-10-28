using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

internal static class DriverTests
{
    private sealed class Resource(Action close) : IDisposable {public void Dispose()=>close();}
    private sealed class Counter;
    private static void Check(bool value,string reason) {if(!value) throw new Exception(reason);}
    private static async Task Throws(Func<Task> action,string reason)
    {
        try {await action();} catch(Exception e) when(e.ToString().Contains(reason,StringComparison.OrdinalIgnoreCase)) {return;}
        throw new Exception("Expected: "+reason);
    }
    private static RuntimeStartup Plan(params RuntimePluginDefinition[] plugins)=>new(1,"test","1",
        plugins.Select(p=>new RuntimePluginSelection(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}))).ToList());
    private static RuntimePluginDefinition Updating(string id,ApplicationUpdate callback,string[]? after=null)=>
        new(id,"1",1,[],[],s=>s.Update("tick",callback)) {Execution=new(Contributions:[new("tick",ApplicationPhase.Update,After:after)])};
    public static async Task Run()
    {
        await ClocksAndViewports();
        await CancellationAndFailures();
        await ScopeLifetimes();
        await InputAndRenderFault();
        await CloseDuringInitialization();
        Console.WriteLine("PASS application scheduling, clocks, input, cancellation and scoped lifecycle");
    }
    private static async Task ClocksAndViewports()
    {
        List<string> trace=[];List<int> batchSizes=[];List<long> rendered=[];List<TimeSpan> display=[];
        var first=Updating("first",(frame,_)=>{batchSizes.Add(frame.Inputs.Count);return ValueTask.FromResult<Action?>(()=>trace.Add("first:"+frame.Tick));});
        var second=Updating("second",(frame,_)=>ValueTask.FromResult<Action?>(()=>trace.Add("second:"+frame.Tick)),["first/tick"]);
        var view=new RuntimePluginDefinition("view","1",1,[],[],s=>s.Render("draw",f=>{rendered.Add(f.CompletedTick);display.Add(f.PresentationTime);}))
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var catalog=new[]{second,view,first};var plan=Plan(catalog);
        await using var host=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,
            clock:new(TimeSpan.FromMilliseconds(10),3,TimeSpan.FromMilliseconds(100)));
        await using var left=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host,"left");
        await using var right=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host,"right");
        var driver=host.Driver;
        Check(driver.Enqueue(new(1,"move",new byte[]{1})),"Input rejected");
        Check(!driver.Enqueue(new(1,"move",new byte[]{1})),"Duplicate input accepted");
        await driver.AdvanceAsync(TimeSpan.FromMilliseconds(55));
        Check(driver.CompletedTick==3 && driver.SimulationTime==TimeSpan.FromMilliseconds(30) && driver.DroppedTime==TimeSpan.FromMilliseconds(20),"Catch-up limit");
        Check(batchSizes.SequenceEqual(new[]{1,0,0}),"Input consumed more than once");
        Check(string.Join(',',trace)=="first:1,second:1,first:2,second:2,first:3,second:3","Update ordering");
        left.Render(TimeSpan.FromMilliseconds(12));right.Render(TimeSpan.FromMilliseconds(20));
        Check(rendered.SequenceEqual(new long[]{3,3}) && driver.CompletedTick==3,"Viewport advanced simulation");
        driver.Paused=true;await driver.AdvanceAsync(TimeSpan.FromSeconds(10));left.Render(TimeSpan.FromMilliseconds(12));
        Check(driver.CompletedTick==3 && display[^1]==TimeSpan.FromMilliseconds(24),"Pause froze presentation");
        Check(driver.Enqueue(new(2,"move",ReadOnlyMemory<byte>.Empty)),"Paused input rejected");
        await driver.StepAsync();Check(driver.CompletedTick==4 && batchSizes[^1]==1,"Single step failed");
        driver.Paused=false;await driver.AdvanceAsync(TimeSpan.FromMilliseconds(1000));
        Check(driver.CompletedTick==7 && driver.DroppedTime==TimeSpan.FromMilliseconds(990),"Long frame accounting");
        await Throws(()=>driver.StepAsync(),"paused");
        await left.DisposeAsync();right.Render(TimeSpan.Zero);Check(driver.CompletedTick==7,"Viewport close changed simulation");
        await using var headless=await RuntimeHost.StartAsync(plan,catalog,[]);
        await headless.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(7));
        Check(headless.Driver.SimulationTime==TimeSpan.FromMilliseconds(7),"Variable logical time");
        await Throws(()=>Task.Run(()=>headless.Render(TimeSpan.Zero)),"viewport");
        await Throws(()=>RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Headless,RuntimeScope.Viewport),"scope");
        await driver.StopAsync();await driver.StopAsync();
        await Throws(()=>driver.AdvanceAsync(TimeSpan.Zero),"Stopped");
    }
    private static async Task CancellationAndFailures()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commits=0;var released=0;
        var slow=Updating("slow",async (_,_)=>{entered.TrySetResult();await finish.Task;return ()=>commits++;});
        var owned=new RuntimePluginDefinition("owned","1",1,[],[],s=>s.Own(new Resource(()=>released++)));
        var host=await RuntimeHost.StartAsync(Plan(slow,owned),[slow,owned],[]);
        var advance=host.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Throws(()=>host.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1)),"in progress");
        var close=host.DisposeAsync().AsTask();
        await Task.Yield();Check(!close.IsCompleted && released==0,"Resources disposed during update");
        finish.SetResult();await close.WaitAsync(TimeSpan.FromSeconds(5));
        await Throws(()=>advance,"cancel");Check(commits==0 && released==1,"Late commit or leaked resource");
        await host.DisposeAsync();Check(released==1,"Double release");

        var prepare=Updating("a",(_,_)=>ValueTask.FromResult<Action?>(()=>commits++));
        var fail=Updating("b",(_,_)=>throw new InvalidOperationException("prepare exploded"),["a/tick"]);
        var drawn=0;
        var draw=new RuntimePluginDefinition("draw","1",1,[],[],s=>s.Render("draw",_=>drawn++))
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var catalog=new[]{prepare,fail,draw};
        await using var failing=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical);
        await using var viewport=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical,RuntimeScope.Viewport,failing);
        await Throws(()=>failing.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1)),"b/tick");
        Check(commits==0 && failing.Driver.CompletedTick==0 && failing.Driver.Fault?.Plugin=="b","Partial preparation published");
        viewport.Render(TimeSpan.FromSeconds(1));Check(drawn==1,"Last good display unavailable after prepare fault");

        fail=Updating("b",(_,_)=>ValueTask.FromResult<Action?>(()=>throw new Exception("commit exploded")),["a/tick"]);
        catalog=[prepare,fail,draw];
        await using var broken=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical);
        await using var brokenViewport=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical,RuntimeScope.Viewport,broken);
        await Throws(()=>broken.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1)),"commit failed");
        await Throws(()=>Task.Run(()=>brokenViewport.Render(TimeSpan.Zero)),"presentation unavailable");

        var alive=0;
        var activation=new RuntimePluginDefinition("activation","1",1,[],[],s=>{alive++;s.Own(new Resource(()=>alive--));s.Update("tick",(_,_)=>ValueTask.FromResult<Action?>(null));throw new Exception("activation exploded");})
            {Execution=new(Contributions:[new("tick",ApplicationPhase.Update)])};
        for(var i=0;i<3;i++)
        {
            await Throws(()=>RuntimeHost.StartAsync(Plan(activation),[activation],[]),"activation exploded");
            Check(alive==0,"Activation rollback leaked");
        }
        var missing=Updating("missing",(_,_)=>ValueTask.FromResult<Action?>(null)) with {Activate=_=>{}};
        await Throws(()=>RuntimeHost.StartAsync(Plan(missing),[missing],[]),"did not register");
        var cycle=Updating("a",(_,_)=>ValueTask.FromResult<Action?>(null),["b/tick"]);
        await Throws(()=>RuntimeHost.StartAsync(Plan(cycle,fail),[cycle,fail],[]),"cycle");
    }
    private static async Task ScopeLifetimes()
    {
        List<string> closed=[];
        var device=new RuntimePluginDefinition("device","1",1,["device"],[],s=>{s.Provide("device",new Counter());s.Own(new Resource(()=>closed.Add("device")));})
            {Execution=new(HostMode.Graphical,RuntimeScope.GraphicsHost)};
        var model=new RuntimePluginDefinition("model","1",1,["model"],["device"],s=>{s.Provide("model",s.Service<Counter>("device"));s.Own(new Resource(()=>closed.Add("model")));});
        var view=new RuntimePluginDefinition("view","1",1,[],["model"],s=>{_ = s.Service<Counter>("model");s.Own(new Resource(()=>closed.Add("view")));s.Render("draw",_=>{});})
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var catalog=new[]{device,model,view};var plan=Plan(catalog);
        await using var graphics=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,RuntimeScope.GraphicsHost);
        var instance=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,RuntimeScope.Instance,graphics);
        var viewport=await RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,instance);
        await instance.DisposeAsync();Check(string.Join(',',closed)=="view,model","Scope release order");
        _=graphics.Service<Counter>("device");
        await viewport.DisposeAsync();await graphics.DisposeAsync();Check(string.Join(',',closed)=="view,model,device","Device lifetime");
        await Throws(()=>RuntimeHost.StartAsync(plan,catalog,[]),"missing capability");
        await Throws(()=>RuntimeHost.StartScopedAsync(plan,catalog,[],HostMode.Graphical),"parent capability");
        var shorter=device with {Requires=["model"]};
        await Throws(()=>RuntimeHost.StartScopedAsync(plan,[shorter,model,view],[],HostMode.Graphical),"shorter-lived");
        var conflict=device with {Id="conflict"};
        await Throws(()=>RuntimeHost.StartScopedAsync(Plan(device,conflict),[device,conflict],[],HostMode.Graphical),"duplicate");
    }
    private static async Task InputAndRenderFault()
    {
        var observed=0;var commits=0;
        var update=Updating("update",(frame,_)=>{observed=frame.Inputs.Single().Data.Span[0];return ValueTask.FromResult<Action?>(()=>commits++);});
        var draw=new RuntimePluginDefinition("draw","1",1,[],[],s=>s.Render("draw",_=>throw new Exception("draw exploded")))
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var catalog=new[]{update,draw};
        await using var instance=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical);
        await using var viewport=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical,RuntimeScope.Viewport,instance);
        byte[] bytes=[7];instance.Driver.Enqueue(new(0,"move",bytes));bytes[0]=9;
        await Throws(()=>Task.Run(()=>viewport.Render(TimeSpan.Zero)),"draw exploded");
        Check(viewport.RenderFault?.Plugin=="draw" && instance.Driver.Fault==null,"Render fault escaped viewport");
        await instance.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1));Check(observed==7 && commits==1,"Input ownership or render isolation");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Throws(()=>instance.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(1),canceled.Token),"cancel");
        Check(instance.Driver.CompletedTick==1,"Canceled tick advanced");
        await Throws(()=>Task.Run(()=>instance.Driver.Enqueue(new(1,"large",new byte[1024*1024+1]))),"budget");
    }
    private static async Task CloseDuringInitialization()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim();var closed=0;
        var root=new RuntimePluginDefinition("root","1",1,["root"],[],s=>{s.Provide("root",new Counter());s.Own(new Resource(()=>closed++));});
        var view=new RuntimePluginDefinition("view","1",1,[],["root"],s=>{entered.SetResult();release.Wait(TimeSpan.FromSeconds(5));_=s.Service<Counter>("root");})
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport)};
        var catalog=new[]{root,view};
        var parent=await RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical);
        var starting=RuntimeHost.StartScopedAsync(Plan(catalog),catalog,[],HostMode.Graphical,RuntimeScope.Viewport,parent);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping=parent.DisposeAsync().AsTask();Check(closed==0,"Dependency released during child initialization");
        release.Set();await Throws(()=>starting,"disposed");await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Check(closed==1,"Parent not released after failed child initialization");
    }
}
