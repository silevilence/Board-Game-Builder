using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal static class RuntimeComposition
{
    public static RuntimeHost Start(LoadedModule original,uint seed,IReadOnlyDictionary<string,string>? overrides,int maxSteps,CancellationToken cancellation=default,SessionRecording? recording=null)
    {
        if(maxSteps<1) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var module=Wire.Copy(original);
        GameRuntime.ValidateSnapshot(module);
        GameEngine? restored=null;
        if(recording!=null)
        {
            if(module.Initial.Flow==null || recording.Switches.Count!=1 || recording.Log.ModuleFingerprint!=module.Fingerprint)
                throw new InvalidDataException("Flow restore content/version mismatch; rebuild the session");
            restored=recording.Replay();
        }
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller);
        foreach(var value in overrides ?? new Dictionary<string,string>())
        {
            if(!controllers.ContainsKey(value.Key) || value.Value is not ("human" or "cpu")) throw new ArgumentException("Invalid seat/controller configuration.");
            controllers[value.Key]=value.Value;
        }
        var startup=module.RuntimeIdentity!.Startup;
        var catalog=Catalog(module,seed,controllers,maxSteps,startup,cancellation,restored,recording);
        return RuntimeHost.StartAsync(startup,catalog,
            AuthorityPlugin.Required.Concat(["rules","session","recording"]).Concat(controllers.Values.Contains("cpu")?["cpu"]:[])
                .Concat(module.RuntimePlugins is {Count:>0}?["effects"]:[])).GetAwaiter().GetResult();
    }
    internal static IReadOnlyList<RuntimePluginDefinition> Catalog(LoadedModule? module=null,uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500,RuntimeStartup? startup=null,CancellationToken cancellation=default,GameEngine? restored=null,SessionRecording? recording=null)
    {
        LoadedModule Module()=>module ?? throw new InvalidOperationException("Session input is required to activate this capability");
        GameDefinition Definition()=>RuntimeDefinition.From(Module());
        RuntimeStartup Startup()=>startup ?? throw new InvalidOperationException("Startup configuration is required to activate this capability");
        var catalog=new List<RuntimePluginDefinition>
        {AuthorityPlugin.Create(Module,seed,restored),FlowRuntimePlugin.Create(Module,cancellation,restored!=null),LuaRuntimePlugins.Rules(Definition,seed),LuaRuntimePlugins.Cpu(Definition),CommandRuleProvider.Plugin,WasmRuntimePlugin.Create(Definition),BoardRenderContract.Description};
        catalog.Add(DefinitionPlugins.Module(Startup));
        catalog.Add(StateDefinitionLoader.Plugin(Startup));
        catalog.Add(BoardRecordingPlugin.Create(Module,recording));
        catalog.AddRange(ResourcePlugins.Catalog(()=>module?.Resources));
        var capabilities=catalog.Where(p=>startup?.Plugins?.Any(s=>s.Id==p.Id)==true).SelectMany(p=>p.Provides).ToHashSet();
        var hasCpu=capabilities.Contains("cpu");var hasEffects=capabilities.Contains("effects");
        var hasFlow=capabilities.Contains("flow");
        catalog.Add(BoardSessionPlugin.Create(Module,seed,controllers,maxSteps,
            new(value=>GameRuntime.ValidateSnapshot(value),RuntimeContent.ScriptFiles),hasCpu,hasEffects,hasFlow));
        return catalog;
    }
}
