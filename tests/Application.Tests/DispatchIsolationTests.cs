using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

internal static class DispatchIsolationTests
{
    private sealed class Resource : IDisposable {public void Dispose() { }}
    private static RuntimeStartup Plan(RuntimePluginDefinition plugin)=>new(1,"dispatch","1",
        [new(plugin.Id,plugin.Version,plugin.Api,JsonSerializer.SerializeToElement(new{}))]);
    public static async Task Run()
    {
        await Check(false);await Check(true);
        Console.WriteLine("PASS graphics activation/cleanup dispatch cannot block unrelated runtime fibers");
    }
    private static async Task Check(bool closing)
    {
        using var release=new ManualResetEventSlim();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block=!closing;
        void Dispatch(Action action) {if(block) {entered.TrySetResult();release.Wait(TimeSpan.FromSeconds(10));}action();}
        var graphics=new RuntimePluginDefinition("graphics","1",1,[],[],scope=>scope.Own(new Resource()))
            {Execution=new(HostMode.Graphical,RuntimeScope.GraphicsHost)};
        var starting=RuntimeHost.StartScopedAsync(Plan(graphics),[graphics],[],HostMode.Graphical,RuntimeScope.GraphicsHost,dispatch:Dispatch);
        RuntimeHost? host=null;Task? stopping=null;RuntimeHost? independent=null;
        if(closing) {host=await starting;block=true;stopping=host.DisposeAsync().AsTask();}
        var other=new RuntimePluginDefinition("other","1",1,["other"],[],scope=>scope.Provide("other",new object()));
        Task<RuntimeHost>? load=null;var completed=false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            load=RuntimeHost.StartAsync(Plan(other),[other],["other"]);
            completed=await Task.WhenAny(load,Task.Delay(1000))==load;
        }
        finally
        {
            release.Set();host ??=await starting;
            if(load!=null)independent=await load;
            if(independent!=null)await independent.DisposeAsync();
            await host.DisposeAsync();if(stopping!=null)await stopping;
        }
        if(!completed)throw new Exception((closing?"Cleanup":"Activation")+" graphics dispatch blocked unrelated content loading");
    }
}
