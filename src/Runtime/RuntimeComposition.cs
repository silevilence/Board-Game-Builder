using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal static class RuntimeComposition
{
    public static RuntimeHost Start(LoadedModule original,uint seed,IReadOnlyDictionary<string,string>? overrides,int maxSteps)
    {
        if(maxSteps<1) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var module=Wire.Copy(original);
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller);
        foreach(var value in overrides ?? new Dictionary<string,string>())
        {
            if(!controllers.ContainsKey(value.Key) || value.Value is not ("human" or "cpu")) throw new ArgumentException("Invalid seat/controller configuration.");
            controllers[value.Key]=value.Value;
        }
        var definition=RuntimeDefinition.From(module);
        var startup=module.RuntimeIdentity?.Startup ?? new(1,module.Id,module.Version,
            [AuthorityPlugin.Selection,LuaRuntimePlugins.Select(module.Rules.Length==0?"rules.commands":"lua.rules"),LuaRuntimePlugins.Select("session.local"),
                ..(module.Cpu.Length==0?Array.Empty<RuntimePluginSelection>():[LuaRuntimePlugins.Select("lua.cpu")]),
                ..(module.RuntimePlugins is not {Count:>0}?Array.Empty<RuntimePluginSelection>():[LuaRuntimePlugins.Select("wasm")])]);
        var catalog=new List<RuntimePluginDefinition>
        {AuthorityPlugin.Create(module,seed),LuaRuntimePlugins.Rules(definition,seed),LuaRuntimePlugins.Cpu(definition),CommandRuleProvider.Plugin,WasmRuntimePlugin.Create(definition)};
        var capabilities=catalog.Where(p=>startup.Plugins.Any(s=>s.Id==p.Id)).SelectMany(p=>p.Provides).ToHashSet();
        var hasCpu=capabilities.Contains("cpu");var hasEffects=capabilities.Contains("effects");
        catalog.Add(new("session.local","1",1,["session"],
            [..AuthorityPlugin.Required,"rules",..(hasCpu?new[]{"cpu"}:[]),..(hasEffects?new[]{"effects"}:[])],scope=>
                scope.Provide("session",scope.Own(new LocalGameSession(module,seed,controllers,maxSteps,
                    scope.Service<IAuthoritySession>("authority"),scope.Service<RuleBinding>("rule-binding"),scope.Service<IRuleProvider>("rules"),
                    hasCpu?scope.Service<ICpuDecisionProvider>("cpu"):null,hasEffects?scope.Service<IEffectProvider>("effects"):null)))));
        return RuntimeHost.StartAsync(startup,catalog,
            AuthorityPlugin.Required.Concat(["rules","session"]).Concat(controllers.Values.Contains("cpu")?["cpu"]:[])
                .Concat(module.RuntimePlugins is {Count:>0}?["effects"]:[])).GetAwaiter().GetResult();
    }
}
