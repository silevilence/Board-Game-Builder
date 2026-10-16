using BoardGame.Editor;
using CordiSharp.Events;

namespace Engine.Tests;

public static class CordiTests
{
    public static void Dispatch()=>DispatchAsync().GetAwaiter().GetResult();
    private static async Task DispatchAsync()
    {
        using var release=new ManualResetEventSlim();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking=false;var released=false;
        await using var app=new EditorApplication(action=>
        {
            if(blocking) {entered.TrySetResult();release.Wait(TimeSpan.FromSeconds(10));}
            action();
        });
        app.Install(new("dispatch-probe",1,[],[],scope=>scope.Own(new Cleanup(()=>released=true))));
        await app.Enable("dispatch-probe");blocking=true;
        var removing=app.Disable("dispatch-probe");
        var independent=CordiSharp.Context.Create();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var other=independent.Inject(Array.Empty<string>(),(_,_)=>Task.CompletedTask);
            await other.Await().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally {release.Set();await removing;await independent.Fiber.DisposeAsync();}
        Check.That(released,"UI cleanup was not awaited");
    }
    public static void Run()=>RunAll().WaitAsync(TimeSpan.FromSeconds(50)).GetAwaiter().GetResult();
    private static async Task RunAll() {await Verify();await InitializationTimeouts();}
    private static async Task InitializationTimeouts()
    {
        await using var app=new EditorApplication();var cancelled=false;var released=false;
        app.Install(new("cooperative",1,[],[],scope=>scope.Own(new Cleanup(()=>released=true)))
        {
            InitializeAsync=async scope=> {try {await Task.Delay(Timeout.Infinite,scope.Cancellation);} finally {cancelled=true;}}
        });
        try {await app.Enable("cooperative");throw new Exception("Initialization did not time out");} catch(TimeoutException) { }
        Check.That(cancelled && released,"Timed-out initializer blocked cleanup");
        var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Task? late=null;var lateResourceDisposed=false;
        app.Install(new("late",1,["late"],[],_=>{})
        {
            InitializeAsync=scope=>late=Finish(scope)
        });
        async Task Finish(EditorPluginScope scope) {await finish.Task;scope.Own(new Cleanup(()=>lateResourceDisposed=true));scope.Provide("late",new object());}
        try {await app.Enable("late");throw new Exception("Initialization did not time out");} catch(TimeoutException) { }
        finish.SetResult();try {await late!;} catch(OperationCanceledException) { }
        Check.That(lateResourceDisposed && app.Root.Get<object>("late")==null,"Late initializer resurrected resources");
        app.Install(new("recovery",1,[],[],_=>{}));await app.Enable("recovery");await app.Disable("recovery");
        var teardown=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Install(new("atomic-uninstall",1,[],[],scope=>scope.Context.Effect(()=> (Func<Task>)(()=>teardown.Task))));
        await app.Enable("atomic-uninstall");
        var removing=app.Uninstall("atomic-uninstall");var reenabling=app.Enable("atomic-uninstall");
        teardown.SetResult();await removing;await Reject(()=>reenabling,"Unknown plugin");
        Check.That(!app.Catalog.Any(p=>p.Id=="atomic-uninstall"),"Concurrent enable revived an uninstalled plugin");
        Check.That(!app.States.Any(p=>p.Id=="atomic-uninstall"),"Unknown plugin created an orphan status row");
    }
    private static async Task Verify()
    {
        await using var app=new EditorApplication();
        var events=new List<string>();var calls=0;var fail=false;
        var ping=EventKey.Create<int>("test.ping");
        foreach(var (id,requires) in new[] {("a",Array.Empty<string>()),("b",new[]{"a"}),("c",new[]{"b"}),("other",Array.Empty<string>())})
            app.Install(new(id,1,[id],requires,c=>
            {
                Trace("+"+id);events.Add("+"+id);c.Own(new Cleanup(()=> {Trace("-"+id);events.Add("-"+id);}));c.Provide(id,new object());
                c.On(ping,_=>calls++);
                if(fail && id=="b") throw new InvalidOperationException("broken b");
            }));
        await app.Enable("other");await app.Enable("c");
        Check.That(string.Join(",",events)=="+other,+a,+b,+c");
        events.Clear();await app.Reload("a");
        Check.That(string.Join(",",events)=="-c,-b,-a,+a,+b,+c");
        for(var i=0;i<3;i++) {await app.Disable("a");await app.Enable("c");}
        app.Root.Emit(ping,1);Check.That(calls==4,"Duplicate event callbacks");
        fail=true;await Reject(()=>app.Reload("a"),"broken b");
        Check.That(app.States.Count(s=>s.Status==EditorPluginStatus.Enabled)==1,"Partially enabled chain");
        app.Root.Emit(ping,1);Check.That(calls==5,"Failed plugin kept event subscription");
        app.Install(new("missing",1,[],["absent"],_=>{}));
        await Reject(()=>app.Enable("missing"),"Missing capability");
        Check.That(app.States.Single(s=>s.Id=="missing").Status==EditorPluginStatus.Waiting);
        app.Install(new("x",1,["x"],["y"],_=>{}));app.Install(new("y",1,["y"],["x"],_=>{}));
        await Reject(()=>app.Enable("x"),"cycle");
        Check.Throws(()=>app.Install(new("bad-api",2,[],[],_=>{})),"API");
        Check.Throws(()=>app.Install(new("duplicate",1,["a"],[],_=>{})),"Duplicate capability");
        var held=true;
        app.Install(new("guarded",1,[],[],c=>c.Guard(()=>held?"活动文档引用":null)));
        await app.Enable("guarded");await Reject(()=>app.Disable("guarded"),"活动文档引用");
        held=false;await app.Disable("guarded");Trace("dispose root");
    }
    private static void Trace(string text) {if(Environment.GetEnvironmentVariable("BGB_TRACE_CORDI")=="1") Console.WriteLine("CORDI "+text);}
    private static async Task Reject(Func<Task> action,string text)
    {
        try {await action();throw new Exception("Expected: "+text);}
        catch(Exception e) when(e.Message.Contains(text) && !e.Message.StartsWith("Expected:")) { }
    }
}
