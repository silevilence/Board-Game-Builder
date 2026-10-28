using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Wasm;

namespace BoardGame.Runtime;

// No WASI, imports, filesystem, network, state handles or hidden authority objects enter a guest.
public sealed class WasmPlugins : IEffectProvider, IDisposable
{
    private readonly WasmExecutor executor;
    private readonly Dictionary<string,RuntimePlugin> plugins=new(StringComparer.Ordinal);
    private readonly object gate=new();
    public IReadOnlyList<string> Ids=>plugins.Keys.ToArray();
    public WasmPlugins(IEnumerable<RuntimePlugin>? definitions)
    {
        WasmExecutor? candidate=null;
        try
        {
            var declared=(definitions ?? []).Take(33).ToArray();
            if(declared.Length>32) throw new InvalidDataException("At most 32 WASM plugins are permitted");
            foreach(var original in declared)
            {
                var d=Wire.Copy(original);
                ModulePackage.ValidateDependencies([new(d.Id,d.Version,d.Api,Kind:"wasm",File:d.File,Sha256:d.Sha256,Permissions:d.Permissions)]);
                if(d.Code.Length>256*1024 || ModulePackage.Hash(d.Code)!=d.Sha256.ToUpperInvariant()) throw new InvalidDataException($"WASM {d.Id}: hash/size mismatch");
                if(!plugins.TryAdd(d.Id,d)) throw new InvalidDataException($"WASM {d.Id}: duplicate identity");
            }
            candidate=new WasmExecutor(plugins.Values.Select(p=>new WasmCode(p.Id,p.Code)));
            foreach(var plugin in plugins.Values)
                candidate.Invoke(plugin.Id,call=>
                {
                    if(call.ReadInt("bgb_api")!=plugin.Api || call.ReadInt("bgb_version").ToString()!=plugin.Version) throw new InvalidDataException($"WASM {plugin.Id}: API/version mismatch");
                    if(call.ReadInt("bgb_init")!=Registration(plugin.Permissions)) throw new InvalidDataException($"WASM {plugin.Id}: registration does not match permissions");
                    return true;
                });
            executor=candidate;
        }
        catch(Exception error) {candidate?.Dispose();throw new InvalidDataException("WASM initialization failed: "+error.Message,error);}
    }
    private static int Registration(List<string> permissions)=>permissions.Sum(p=>p switch {"component"=>1,"command"=>2,"snap"=>4,"container"=>8,_=>0});
    public Command Expand(SeatView view,Command command)
    {
        if(!command.Kind.StartsWith("wasm.",StringComparison.Ordinal)) return command;
        lock(gate)
        {
            var parts=command.Kind.Split('.');
            if(parts.Length!=3 || !plugins.TryGetValue(parts[1],out var plugin) || !plugin.Permissions.Contains(parts[2])) throw new InvalidDataException("WASM command permission missing");
            var entity=view.Entities.SingleOrDefault(e=>e.Id==command.Entity) ?? throw new InvalidDataException("WASM target is not visible");
            try
            {
                return executor.Invoke(plugin.Id,call=>
                {
                if(call.ReadInt("bgb_init")!=Registration(plugin.Permissions)) throw new InvalidDataException("Registration mismatch");
                int Apply(int first,int second)=>call.ApplyInt("bgb_"+parts[2],first,second);
                var property="wasm."+parts[1];
                return parts[2] switch
                {
                    "command" when entity.Properties.ContainsKey(property)=>new Command {Kind="set",Entity=entity.Id,Property=property,Value=Apply(entity.Properties[property],command.Value)},
                    "snap"=>new Command {Kind="move",Entity=entity.Id,X=Apply(checked((int)entity.X),checked((int)command.X)),Y=Apply(checked((int)entity.Y),checked((int)command.Y))},
                    "container" when entity.IsContainer=>new Command {Kind="resize-container",Entity=entity.Id,Value=Apply(entity.Count,command.Value)},
                    _=>throw new InvalidDataException("Unsupported WASM capability or target component")
                };
                });
            }
            catch(Exception error) {throw new InvalidDataException($"WASM {plugin.Id}:{parts[2]}: {error.Message}",error);}
        }
    }
    public void Dispose() {lock(gate) {plugins.Clear();executor.Dispose();}}
}

public static class WasmRuntimePlugin
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
