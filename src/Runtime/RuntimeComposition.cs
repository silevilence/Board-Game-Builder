using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal static class RuntimeComposition
{
    public static RuntimeHost Start(LoadedModule original,uint seed,IReadOnlyDictionary<string,string>? overrides,int maxSteps)
    {
        if(maxSteps<1) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var module=Wire.Copy(original);
        GameRuntime.ValidateSnapshot(module);
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller);
        foreach(var value in overrides ?? new Dictionary<string,string>())
        {
            if(!controllers.ContainsKey(value.Key) || value.Value is not ("human" or "cpu")) throw new ArgumentException("Invalid seat/controller configuration.");
            controllers[value.Key]=value.Value;
        }
        var startup=module.RuntimeIdentity!.Startup;
        var catalog=Catalog(module,seed,controllers,maxSteps,startup);
        return RuntimeHost.StartAsync(startup,catalog,
            AuthorityPlugin.Required.Concat(["rules","session","recording"]).Concat(controllers.Values.Contains("cpu")?["cpu"]:[])
                .Concat(module.RuntimePlugins is {Count:>0}?["effects"]:[])).GetAwaiter().GetResult();
    }
    internal static IReadOnlyList<RuntimePluginDefinition> Catalog(LoadedModule? module=null,uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500,RuntimeStartup? startup=null)
    {
        LoadedModule Module()=>module ?? throw new InvalidOperationException("Session input is required to activate this capability");
        GameDefinition Definition()=>RuntimeDefinition.From(Module());
        RuntimeStartup Startup()=>startup ?? throw new InvalidOperationException("Startup configuration is required to activate this capability");
        var catalog=new List<RuntimePluginDefinition>
        {AuthorityPlugin.Create(Module,seed),FlowController.Plugin(Module),LuaRuntimePlugins.Rules(Definition,seed),LuaRuntimePlugins.Cpu(Definition),CommandRuleProvider.Plugin,WasmRuntimePlugin.Create(Definition)};
        catalog.Add(DefinitionPlugins.Module(Startup));
        catalog.Add(StateDefinitionLoader.Plugin(Startup));
        catalog.Add(SessionRecorder.Plugin(Module));
        catalog.AddRange(ResourcePlugins.Catalog(()=>module?.Resources));
        var capabilities=catalog.Where(p=>startup?.Plugins?.Any(s=>s.Id==p.Id)==true).SelectMany(p=>p.Provides).ToHashSet();
        var hasCpu=capabilities.Contains("cpu");var hasEffects=capabilities.Contains("effects");
        var hasFlow=capabilities.Contains("flow");
        catalog.Add(new("session.local","1",1,["session"],
            [..AuthorityPlugin.Required,"rules","recording",..(hasCpu?new[]{"cpu"}:[]),..(hasEffects?new[]{"effects"}:[]),..(hasFlow?new[]{"flow"}:[])],scope=>
                scope.Provide("session",scope.Own(new LocalGameSession(Module(),seed,controllers ?? throw new InvalidOperationException("Session controllers are required"),maxSteps,
                    scope.Service<IAuthoritySession>("authority"),scope.Service<RuleBinding>("rule-binding"),scope.Service<IRuleProvider>("rules"),
                    hasCpu?scope.Service<ICpuDecisionProvider>("cpu"):null,hasEffects?scope.Service<IEffectProvider>("effects"):null,scope.Service<IRecordingProvider>("recording"),hasFlow?scope.Service<FlowController>("flow"):null)))));
        return catalog;
    }
}
