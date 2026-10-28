using System.Text.Json;
using BoardGame.Lua;

namespace Engine.Tests;

public static class LuaExecutorTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        Check.That(!typeof(LuaExecution).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "Runtime" or "Kernel" or "Kernel.Abstractions" or "BoardGame.Core"),"Generic executor references board contracts");
        const string source="return {sample=function(c) return {value=c.position+c.velocity*c.elapsed,points={},commands={}} end}";
        using var process=new LuaProcessSession(typeof(LuaWorker).Assembly.Location);
        Check.That(process.ProcessId==null,"Executor eagerly started process");
        var request=new LuaRequest(source,"motion.lua","sample","{\"position\":2,\"velocity\":3,\"elapsed\":4}",1,new(ArrayFields:["points"]));
        using var result=JsonDocument.Parse(await process.InvokeAsync(request));
        Check.That(result.RootElement.GetProperty("value").GetDouble()==14);
        Check.That(result.RootElement.GetProperty("points").ValueKind==JsonValueKind.Array && result.RootElement.GetProperty("commands").ValueKind==JsonValueKind.Object,"Executor guessed board result fields");
        Check.That(await process.InvokeAsync(request with {Shape=new(ValidateExports:["sample"])})=="true");
        var legacy=LuaEvaluator.Invoke("return {actions=function(c) return {} end}","legacy.lua","actions","{}");
        Check.That(legacy=="[]","Legacy binding changed empty action result");
        using var other=new LuaProcessSession(typeof(LuaWorker).Assembly.Location);
        const string mutable="local n=0; return {sample=function(c) n=n+1; return n end}";
        for(var i=0;i<3;i++)
        {
            Check.That(await process.InvokeAsync(new(mutable,"scope.lua","sample","{}",42))=="1");
            Check.That(await other.InvokeAsync(new(mutable,"scope.lua","sample","{}",42))=="1");
        }
        Check.That(process.ProcessId!=other.ProcessId,"Instances shared execution process");
        Check.Throws(()=>process.InvokeAsync(request with {Api=1}).GetAwaiter().GetResult(),"protocol");
    }
}
