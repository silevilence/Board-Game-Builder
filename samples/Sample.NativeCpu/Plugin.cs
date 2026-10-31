using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Sample.NativeCpu;

/// <summary>A deliberately small external policy, configured by the sample game.</summary>
public static class Plugin
{
    public static ApprovedApplicationPlugin Create()=>new(_=>new("sample.native-cpu","1",1,["cpu"],[],scope=>
        scope.Provide("cpu",new Policy(scope.Configuration.GetProperty("action").GetString()!))) {Content=new(["action"])});
    private sealed class Policy(string action):ICpuDecisionProvider
    {
        public string Entry=>"sample.native-cpu/"+action;
        public Command Decide(SeatView view,IReadOnlyList<ActionOffer> actions,uint random)=>actions.Single(p=>p.Id==action).Command;
        public Action Prepare(GameDefinition definition)=>()=>{};
    }
}
