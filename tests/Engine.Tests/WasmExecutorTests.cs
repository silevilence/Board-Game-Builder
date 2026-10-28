using BoardGame.Wasm;
using Wasmtime;

namespace Engine.Tests;

public static class WasmExecutorTests
{
    public static void Run()
    {
        Check.That(!typeof(WasmExecutor).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "Kernel" or "Kernel.Abstractions" or "Runtime" or "BoardGame.Core"),"WASM executor depends on board data");
        var bytes=Module.ConvertText("""
        (module
          (global $count (mut i32) (i32.const 0))
          (func (export "sum") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add)
          (func (export "count") (result i32) global.get $count i32.const 1 i32.add global.set $count global.get $count))
        """);
        using var executor=new WasmExecutor([new("arithmetic",bytes)]);
        WasmCall? retained=null;
        for(var i=0;i<3;i++)
            Check.That(executor.Invoke("arithmetic",call=>{retained=call;return call.ApplyInt("sum",3,4)==7 && call.ReadInt("count")==1;}) ,"WASM store state leaked between calls");
        Check.Throws(()=>retained!.ReadInt("count"),"disposed");
        Check.Throws(()=>executor.Invoke("arithmetic",call=>call.ReadInt("missing")),"Missing WASM export");
        executor.Dispose();executor.Dispose();Check.Throws(()=>executor.Invoke("arithmetic",call=>0),"disposed");
        var imported=Module.ConvertText("(module (import \"host\" \"state\" (func)))");
        Check.Throws(()=>new WasmExecutor([new("imports",imported)]),"imports");
        Check.Throws(()=>new WasmExecutor([new("duplicate",bytes),new("duplicate",bytes)]),"duplicate");
        Check.Throws(()=>new WasmExecutor([new("too-big",bytes)],new(MaxModuleBytes:1)),"budget");
        foreach(var (body,expected) in new[]
        {
            ("(memory 1) (func (export \"trap\") (result i32) i32.const 1000000 i32.load)",TrapCode.MemoryOutOfBounds),
            ("(func (export \"trap\") (result i32) unreachable)",TrapCode.Unreachable),
            ("(func (export \"trap\") (result i32) i32.const 1 i32.const 0 i32.div_s)",TrapCode.IntegerDivisionByZero),
            ("(func $f (export \"trap\") (result i32) call $f)",TrapCode.StackOverflow)
        })
        {
            using var traps=new WasmExecutor([new("trap",Module.ConvertText("(module "+body+")"))],new(Fuel:10_000_000));
            try {traps.Invoke("trap",call=>call.ReadInt("trap"));throw new Exception("Expected a catchable WASM trap: "+expected);}
            catch(TrapException error) {Check.That(error.Type==expected,"Wrong WASM trap: "+error.Type);}
        }
    }
}
