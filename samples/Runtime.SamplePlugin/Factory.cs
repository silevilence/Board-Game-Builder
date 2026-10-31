using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace Example.RuntimePlugin;

// No board-game contract or editor dependency; the same explicit factory is
// called by the ordinary importer and by generated NativeAOT registration.
public static class Factory
{
    public static ApprovedApplicationPlugin Create()=>new(_=>new("sample.counter","1",1,["sample.counter"],[],scope=>
    {
        var counter=new Counter();scope.Provide("sample.counter",counter);
        scope.Update("tick",(_,_)=>ValueTask.FromResult<Action?>(()=>counter.Ticks++));
    }){Execution=new(HostMode.All,RuntimeScope.Instance,[new("tick",ApplicationPhase.Update)])});
}
public sealed class Counter {public int Ticks {get;internal set;}}
