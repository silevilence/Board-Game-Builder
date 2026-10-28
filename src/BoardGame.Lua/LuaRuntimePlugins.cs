using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Lua;

namespace BoardGame.Runtime;

internal sealed class LuaRuleProvider : IRuleProvider, IDisposable,ICancellableExecution
{
    private readonly InvocationCancellation calls=new();
    public IDisposable Enter(CancellationToken token)=>calls.Enter(token);
    private readonly LuaWorkerSession worker=new();
    private readonly uint seed;
    public IGameRules Rules {get;private set;}
    public LuaRuleProvider(GameDefinition definition,uint seed)
    {
        this.seed=seed;
        var script=RuntimeDefinition.Configuration<LuaScript>(definition,"lua.rules");
        Rules=new LuaRules(script.Source,script.Path,seed,worker,()=>calls.Token);
    }
    public IGameRules Prepare(GameDefinition definition)
    {
        var script=RuntimeDefinition.Configuration<LuaScript>(definition,"lua.rules");
        using var probe=new LuaWorkerSession();
        probe.Invoke(script.Source,script.Path,"__validate_rules","{}",seed);
        return new LuaRules(script.Source,script.Path,seed,worker,()=>calls.Token);
    }
    public void Apply(IGameRules rules)=>Rules=rules;
    public void Dispose()=>worker.Dispose();
}

internal sealed class LuaCpuProvider(GameDefinition definition) : ICpuDecisionProvider, IDisposable,ICancellableExecution
{
    private readonly InvocationCancellation calls=new();
    public IDisposable Enter(CancellationToken token)=>calls.Enter(token);
    private readonly LuaWorkerSession worker=new();
    private LuaScript script=RuntimeDefinition.Configuration<LuaScript>(definition,"lua.cpu");
    public string Entry=>script.Path+":decide";
    public Command Decide(SeatView view,IReadOnlyList<ActionOffer> actions,uint random)=>
        Wire.Read<Command>(worker.Invoke(script.Source,script.Path,"decide",Wire.Json(new {view,actions}),random,calls.Token));
    public Action Prepare(GameDefinition definition)
    {
        var candidate=RuntimeDefinition.Configuration<LuaScript>(definition,"lua.cpu");
        using var probe=new LuaWorkerSession();
        probe.Invoke(candidate.Source,candidate.Path,"__validate_cpu","{}",1);
        return ()=>script=candidate;
    }
    public void Dispose()=>worker.Dispose();
}

public static class LuaRuntimePlugins
{
    private static PluginContentContract ScriptContent=>new(["file"],(config,_)=>ContentPath.Validate(config.GetProperty("file").GetString()!),
        Files:config=>[config.GetProperty("file").GetString()!]);
    public static RuntimePluginDefinition Rules(Func<GameDefinition> definition,uint seed)=>new("lua.rules","1",1,["rules"],[],scope=>
        scope.Provide("rules",scope.Own(new LuaRuleProvider(definition(),seed)))) {Content=ScriptContent};
    public static RuntimePluginDefinition Cpu(Func<GameDefinition> definition)=>new("lua.cpu","1",1,["cpu"],[],scope=>
        scope.Provide("cpu",scope.Own(new LuaCpuProvider(definition())))) {Content=ScriptContent};
}
