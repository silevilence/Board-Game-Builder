using BoardGame.Kernel;

namespace BoardGame.Runtime;

public sealed record BoardContentPolicy(Action<LoadedModule> Validate,Func<RuntimeStartup,IEnumerable<string>> ReloadableFiles);

public static class BoardSessionPlugin
{
    public static RuntimePluginDefinition Create(Func<LoadedModule> module,uint seed,IReadOnlyDictionary<string,string>? controllers,int maxSteps,
        BoardContentPolicy content,bool hasCpu=false,bool hasEffects=false,bool hasFlow=false)=>
        new("session.local","1",1,["session"],
            [..AuthorityPlugin.Required,"board-preparation","rules","recording",..(hasCpu?new[]{"cpu"}:[]),..(hasEffects?new[]{"effects"}:[]),..(hasFlow?new[]{"flow"}:[])],scope=>
        {
            var session=scope.Own(new LocalGameSession(module(),seed,controllers ?? throw new InvalidOperationException("Session controllers are required"),maxSteps,
                scope.Service<IAuthoritySession>("authority"),scope.Service<RuleBinding>("rule-binding"),scope.Service<IRuleProvider>("rules"),
                hasCpu?scope.Service<ICpuDecisionProvider>("cpu"):null,hasEffects?scope.Service<IEffectProvider>("effects"):null,
                scope.Service<IRecordingProvider>("recording"),scope.Service<IBoardTransactionSource>("board-preparation"),content,
                hasFlow?scope.Service<IFlowProgression>("flow"):null));
            scope.Provide("session",session);scope.Update("advance",session.PrepareUpdate);
        }) {Permissions=[AuthorityPlugin.Access],ServicePermissions=new Dictionary<string,string>{{"session",AuthorityPlugin.Access}},
            Execution=new(Contributions:[new("advance",ApplicationPhase.Update)])};
}
