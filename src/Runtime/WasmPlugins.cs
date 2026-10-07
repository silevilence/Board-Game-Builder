using BoardGame.Kernel;
using BoardGame.Modules;
using Wasmtime;

namespace BoardGame.Runtime;

// No WASI, imports, filesystem, network, state handles or hidden authority objects enter a guest.
public sealed class WasmPlugins : IDisposable
{
    private readonly Engine engine;
    private readonly Dictionary<string,(RuntimePlugin Definition,Module Module)> plugins=new(StringComparer.Ordinal);
    public IReadOnlyList<string> Ids=>plugins.Keys.ToArray();
    public WasmPlugins(IEnumerable<RuntimePlugin>? definitions)
    {
        using var config=new Config().WithFuelConsumption(true);
        engine=new Engine(config);
        try
        {
            foreach(var original in definitions ?? [])
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
                    if(Read("bgb_init")!=0) throw new InvalidDataException($"WASM {d.Id}: unsupported registration");
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
    public void Dispose() {foreach(var plugin in plugins.Values) plugin.Module.Dispose();plugins.Clear();engine.Dispose();}
}
