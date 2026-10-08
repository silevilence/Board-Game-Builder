using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class RuntimeHostTests
{
    private sealed class Resource(Action release) : IDisposable {public void Dispose()=>release();}
    private static RuntimeStartup Plan(params string[] ids)=>new(1,"test","1",ids.Select(id=>new RuntimePluginSelection(id,"1",1,JsonSerializer.SerializeToElement(new {}))).ToList());
    public static void Run()=>Verify().GetAwaiter().GetResult();
    private static async Task Verify()
    {
        var alive=0;var order=new List<string>();
        RuntimePluginDefinition Plugin(string id,string[] dependencies)=>new(id,"1",1,[id],dependencies,scope=>
        {
            foreach(var dependency in dependencies) _=scope.Service<object>(dependency);
            alive++;scope.Own(new Resource(()=>{alive--;order.Add(id);}));scope.Provide(id,new object());
        });
        var catalog=new[] {Plugin("a",[]),Plugin("b",["a"]),Plugin("c",["b"])};
        for(var iteration=0;iteration<3;iteration++)
        {
            var first=await RuntimeHost.StartAsync(Plan("c","a","b"),catalog,["c"]);
            using var second=await RuntimeHost.StartAsync(Plan("a","b","c"),catalog,["c"]);
            Check.That(alive==6 && !ReferenceEquals(first.Service<object>("a"),second.Service<object>("a")),"Runtime roots shared service instances");
            order.Clear();await first.DisposeAsync();await first.DisposeAsync();
            Check.That(alive==3 && string.Join(",",order)=="c,b,a","Incorrect reverse dependency cleanup");
            Check.Throws(()=>first.Service<object>("a"),"disposed");_=second.Service<object>("c");
        }
        Check.That(alive==0,"Repeated roots leaked resources");
        Check.Throws(()=>RuntimeHost.Validate(Plan("x"),catalog,[]),"unapproved");
        Check.Throws(()=>RuntimeHost.Validate(Plan("a","a"),catalog,[]),"duplicate");
        Check.Throws(()=>RuntimeHost.Validate(Plan("a"),catalog,["projection"]),"required capability");
        Check.Throws(()=>RuntimeHost.Validate(Plan("c"),catalog,[]),"missing capability");
        var wrong=Plan("a");wrong.Plugins[0]=wrong.Plugins[0] with {Version="2"};
        Check.Throws(()=>RuntimeHost.Validate(wrong,catalog,[]),"version");
        Check.Throws(()=>RuntimeHost.Validate(Plan("a","b"),[Plugin("a",["b"]),Plugin("b",["a"])],[]),"cycle");
        Check.Throws(()=>RuntimeHost.Validate(Plan("a","b"),[catalog[0],catalog[1] with {Provides=["a"]}],[]),"duplicate");
        var failing=catalog.ToArray();failing[1]=failing[1] with {Activate=scope=>
        {alive++;scope.Own(new Resource(()=>alive--));scope.Provide("b",new object());throw new InvalidOperationException("injected failure");}};
        for(var i=0;i<3;i++)
        {
            Check.Throws(()=>RuntimeHost.StartAsync(Plan("a","b","c"),failing,["c"]).GetAwaiter().GetResult(),"injected failure");
            Check.That(alive==0,"Failed initialization leaked registration/resource");
        }
        Check.Throws(()=>RuntimeHost.StartAsync(Plan("a"),[catalog[0] with {Activate=_=>{}}],["a"]).GetAwaiter().GetResult(),"did not provide");
        RuntimePluginScope? retained=null;
        var owned=await RuntimeHost.StartAsync(Plan("a"),[catalog[0] with {Activate=s=>{retained=s;s.Provide("a",new object());}}],["a"]);
        await Task.WhenAll(owned.DisposeAsync().AsTask(),owned.DisposeAsync().AsTask());
        Check.Throws(()=>retained!.Provide("a",new object()),"disposed");
        var released=false;
        Check.Throws(()=>retained!.Own(new Resource(()=>released=true)),"disposed");
        Check.That(released,"Late registration leaked resource");
        Check.Throws(()=>RuntimeHost.Validate(Plan("a"),[catalog[0] with {Requires=[""]}],[]),"invalid capability");
        Check.Throws(()=>new PlaySession(new LoadedModule("legacy","1","",CommandTests.Initial())),"runtime identity");
        var minimal=GameRuntime.Load("samples/state-minimal");
        using(var session=new PlaySession(minimal))
        {
            Check.That(session.Actions().Count==1 && session.Fault==null,"No-script combination invoked Lua");
            Check.That(!session.SubmitHuman("p1",new(){Kind="next"}).Accepted,"Unconfigured action bypassed rules");
        }
        Check.Throws(()=>new PlaySession(minimal,controllers:new Dictionary<string,string>{{"p1","cpu"}}),"required capability: cpu");
    }
}
