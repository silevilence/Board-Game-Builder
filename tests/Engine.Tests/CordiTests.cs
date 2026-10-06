using BoardGame.Editor;
using CordiSharp.Events;

namespace Engine.Tests;

public static class CordiTests
{
    public static void Run()=>Verify().WaitAsync(TimeSpan.FromSeconds(45)).GetAwaiter().GetResult();
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
