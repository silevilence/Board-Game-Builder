using Wasmtime;

namespace BoardGame.Wasm;

public sealed record WasmCode(string Id,ReadOnlyMemory<byte> Bytes);
public sealed record WasmBudget(ulong Fuel=100_000,long MemoryBytes=4*1024*1024,int MaxModules=32,int MaxModuleBytes=256*1024);

/// <summary>Compiled code is shared within one owner; every invocation receives a fresh store.
/// No imports, WASI or host state handles are exposed.</summary>
public sealed class WasmExecutor : IDisposable
{
    private readonly Engine engine;
    private readonly WasmBudget budget;
    private readonly Dictionary<string,Module> modules=new(StringComparer.Ordinal);
    private readonly object gate=new();
    private bool disposed;
    public WasmExecutor(IEnumerable<WasmCode> code,WasmBudget? budget=null)
    {
        this.budget=budget ?? new();
        if(this.budget.Fuel==0 || this.budget.MemoryBytes<=0 || this.budget.MaxModules is <1 or >1024 || this.budget.MaxModuleBytes<1)
            throw new ArgumentOutOfRangeException(nameof(budget));
        using var configuration=new Config().WithFuelConsumption(true);
        WasmtimeTrapCompatibility.UseExplicitChecks(configuration);
        engine=new Engine(configuration);
        try
        {
            foreach(var item in code)
            {
                if(modules.Count>=this.budget.MaxModules || item.Bytes.Length>this.budget.MaxModuleBytes) throw new InvalidDataException("WASM module count/size budget exceeded");
                if(string.IsNullOrWhiteSpace(item.Id) || modules.ContainsKey(item.Id)) throw new InvalidDataException("Invalid/duplicate WASM identity");
                var module=Module.FromBytes(engine,item.Id,item.Bytes.ToArray());
                if(module.Imports.Count!=0) {module.Dispose();throw new InvalidDataException(item.Id+": imports are not permitted");}
                modules.Add(item.Id,module);
            }
        }
        catch {Dispose();throw;}
    }
    public T Invoke<T>(string id,Func<WasmCall,T> operation)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(!modules.TryGetValue(id,out var module)) throw new InvalidDataException("Unknown WASM module: "+id);
            using var store=new Store(engine);
            store.SetLimits(memorySize:budget.MemoryBytes,tableElements:1024,instances:1,tables:1,memories:1);store.Fuel=budget.Fuel;
            using var linker=new Linker(engine);var instance=linker.Instantiate(store,module);
            var call=new WasmCall(instance);
            try {return operation(call);}
            finally {call.Close();}
        }
    }
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed) return;disposed=true;
            foreach(var module in modules.Values) module.Dispose();modules.Clear();engine.Dispose();
        }
    }
}
public sealed class WasmCall
{
    private Instance? instance;
    internal WasmCall(Instance instance)=>this.instance=instance;
    internal void Close()=>instance=null;
    private Instance Active=>instance ?? throw new ObjectDisposedException(nameof(WasmCall));
    private Function Require(string export,int parameters)
    {
        var function=Active.GetFunction(export);
        if(function==null || function.Parameters.Count!=parameters || function.Parameters.Any(k=>k!=ValueKind.Int32) ||
            function.Results.Count!=1 || function.Results[0]!=ValueKind.Int32)
            throw new InvalidDataException("Missing WASM export or incompatible i32 signature: "+export);
        return function;
    }
    public int ReadInt(string export)=>(int)Require(export,0).Invoke()!;
    public int ApplyInt(string export,int first,int second)=>(int)Require(export,2).Invoke(new ValueBox[]{first,second})!;
}
