using System.Diagnostics;

namespace BoardGame.Lua;

// Compatibility binding for the existing board rule/CPU/flow API.
internal static class BoardLuaBinding
{
    internal static LuaCallShape Shape(string entry)=>new(entry=="actions",["commands","winners"],
        entry=="__validate_rules"?["actions","decide"]:entry=="__validate_cpu"?["decide"]:null);
}
public static class LuaWorker
{
    internal static ActivitySource Diagnostics=>LuaProcessSession.Diagnostics;
    public static string Invoke(string source,string path,string entry,string input,uint seed=1)
    {using var worker=new LuaWorkerSession();return worker.Invoke(source,path,entry,input,seed);}
}
public sealed class LuaWorkerSession : IDisposable
{
    private readonly LuaProcessSession process=new(typeof(LuaWorker).Assembly.Location,Path.Combine(AppContext.BaseDirectory,"worker","Lua.exe"));
    internal int? ProcessId=>process.ProcessId;
    public string Invoke(string source,string path,string entry,string input,uint seed=1,CancellationToken cancellation=default)=>
        process.InvokeAsync(new(source,path,entry,input,seed,BoardLuaBinding.Shape(entry)),cancellation).GetAwaiter().GetResult();
    public void Dispose()=>process.Dispose();
}
public static class LuaEvaluator
{
    public static string Invoke(string source,string path,string entry,string input,uint seed=1)=>
        LuaExecution.Invoke(source,path,entry,input,seed,BoardLuaBinding.Shape(entry));
}
