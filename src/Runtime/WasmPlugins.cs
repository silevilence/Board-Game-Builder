using BoardGame.Kernel;
using BoardGame.Modules;
using Wasmtime;

namespace BoardGame.Runtime;

// No WASI, imports, filesystem, network, state handles or hidden authority objects enter a guest.
public sealed class WasmPlugins : IEffectProvider, IDisposable
{
    private readonly Engine engine;
    private readonly Dictionary<string,(RuntimePlugin Definition,Module Module)> plugins=new(StringComparer.Ordinal);
    private readonly object gate=new();
    public IReadOnlyList<string> Ids=>plugins.Keys.ToArray();
    public WasmPlugins(IEnumerable<RuntimePlugin>? definitions)
    {
        using var config=new Config().WithFuelConsumption(true);
        engine=new Engine(config);
        try
        {
            var declared=(definitions ?? []).Take(33).ToArray();
            if(declared.Length>32) throw new InvalidDataException("At most 32 WASM plugins are permitted");
            foreach(var original in declared)
            {
                var d=Wire.Copy(original);
                ModulePackage.ValidateDependencies([new(d.Id,d.Version,d.Api,Kind:"wasm",File:d.File,Sha256:d.Sha256,Permissions:d.Permissions)]);
                if(d.Code.Length>256*1024 || ModulePackage.Hash(d.Code)!=d.Sha256.ToUpperInvariant()) throw new InvalidDataException($"WASM {d.Id}: hash/size mismatch");
                var module=Module.FromBytes(engine,d.Id,d.Code);
                try
                {
                    if(module.Imports.Count!=0) throw new InvalidDataException($"WASM {d.Id}: imports are not permitted");
                    if(!plugins.TryAdd(d.Id,(d,module))) throw new InvalidDataException($"WASM {d.Id}: duplicate identity");
                    using var store=CreateStore();using var linker=new Linker(engine);var instance=linker.Instantiate(store,module);
                    int Read(string name)=>(instance.GetFunction<int>(name) ?? throw new InvalidDataException($"WASM {d.Id}: missing {name}"))();
                    if(Read("bgb_api")!=d.Api || Read("bgb_version").ToString()!=d.Version) throw new InvalidDataException($"WASM {d.Id}: API/version mismatch");
                    if(Read("bgb_init")!=Registration(d.Permissions)) throw new InvalidDataException($"WASM {d.Id}: registration does not match permissions");
                }
                catch {if(!plugins.Values.Any(p=>ReferenceEquals(p.Module,module))) module.Dispose();throw;}
            }
        }
        catch(Exception error) {Dispose();throw new InvalidDataException("WASM initialization failed: "+error.Message,error);}
    }
    private Store CreateStore()
    {
        var store=new Store(engine);store.SetLimits(memorySize:4*1024*1024,tableElements:1024,instances:1,tables:1,memories:1);store.Fuel=100_000;return store;
    }
    private static int Registration(List<string> permissions)=>permissions.Sum(p=>p switch {"component"=>1,"command"=>2,"snap"=>4,"container"=>8,_=>0});
    public Command Expand(SeatView view,Command command)
    {
        if(!command.Kind.StartsWith("wasm.",StringComparison.Ordinal)) return command;
        lock(gate)
        {
            var parts=command.Kind.Split('.');
            if(parts.Length!=3 || !plugins.TryGetValue(parts[1],out var plugin) || !plugin.Definition.Permissions.Contains(parts[2])) throw new InvalidDataException("WASM command permission missing");
            var entity=view.Entities.SingleOrDefault(e=>e.Id==command.Entity) ?? throw new InvalidDataException("WASM target is not visible");
            try
            {
                using var store=CreateStore();using var linker=new Linker(engine);var instance=linker.Instantiate(store,plugin.Module);
                if(instance.GetFunction<int>("bgb_init")!()!=Registration(plugin.Definition.Permissions)) throw new InvalidDataException("Registration mismatch");
                var apply=instance.GetFunction<int,int,int>("bgb_"+parts[2]) ?? throw new InvalidDataException("Missing capability export");
                var property="wasm."+parts[1];
                return parts[2] switch
                {
                    "command" when entity.Properties.ContainsKey(property)=>new() {Kind="set",Entity=entity.Id,Property=property,Value=apply(entity.Properties[property],command.Value)},
                    "snap"=>new() {Kind="move",Entity=entity.Id,X=apply(checked((int)entity.X),checked((int)command.X)),Y=apply(checked((int)entity.Y),checked((int)command.Y))},
                    "container" when entity.IsContainer=>new() {Kind="resize-container",Entity=entity.Id,Value=apply(entity.Count,command.Value)},
                    _=>throw new InvalidDataException("Unsupported WASM capability or target component")
                };
            }
            catch(Exception error) {throw new InvalidDataException($"WASM {plugin.Definition.Id}:{parts[2]}: {error.Message}",error);}
        }
    }
    public void Dispose() {lock(gate) {foreach(var plugin in plugins.Values) plugin.Module.Dispose();plugins.Clear();engine.Dispose();}}
}

internal sealed class EffectRules(IGameRules rules,IEffectProvider plugins) : IGameRules
{
    public IReadOnlyList<ActionOffer> Actions(SeatView view)=>rules.Actions(view);
    public RuleDecision Decide(SeatView view,Command command)
    {
        var decision=rules.Decide(view,command);
        if(!decision.Allowed) return decision;
        // Expansion is pure: the Kernel applies the entire resulting batch or none of it.
        return decision with {Commands=decision.Commands.Select(c=>plugins.Expand(view,c)).ToList()};
    }
}

internal static class WasmRuntimePlugin
{
    public static RuntimePluginDefinition Create(Func<GameDefinition> definition)=>new("wasm","1",1,["effects"],[],scope=>
        scope.Provide("effects",scope.Own(new WasmPlugins(RuntimeDefinition.Configuration<List<RuntimePlugin>>(definition(),"wasm")))))
        {Content=new(["extensions"],(config,startup)=>
        {
            var extensions=System.Text.Json.JsonSerializer.Deserialize<List<RuntimeDependency>>(config.GetProperty("extensions"),Wire.Options) ?? throw new InvalidDataException("wasm: missing extensions");
            if(extensions.Any(p=>p.Kind!="wasm" || p.EditorOnly)) throw new InvalidDataException("wasm: expected runtime WASM extension");
            ModulePackage.ValidateDependencies(extensions);
        },Files:config=>System.Text.Json.JsonSerializer.Deserialize<List<RuntimeDependency>>(config.GetProperty("extensions"),Wire.Options)!.Select(p=>p.File!))};
}
