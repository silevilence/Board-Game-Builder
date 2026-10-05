using System.Buffers.Binary;
using System.Diagnostics;
using BoardGame.Lua;
namespace Engine.Tests;

public static class WorkerTests
{
    private const string Constant="return {decide=function(c) return 7 end}";
    private const string NativeLoop="return {decide=function(c) local s=string.rep('a',1000000); while true do s=string.reverse(s) end end}";
    public static void Run()
    {
        using var worker=new LuaWorkerSession();
        Check.That(worker.Invoke(Constant,"value.lua","decide","{}")=="7");
        var first=worker.ProcessId!.Value;
        const string globals="local n=0; return {decide=function(c) n=n+1; secret=c.secret; math.private=c.secret; return {n=n,random=random_index(1000000)} end}";
        var a=worker.Invoke(globals,"fresh.lua","decide","{\"secret\":\"PRIVATE\"}",42);
        var b=worker.Invoke(globals,"fresh.lua","decide","{\"secret\":\"OTHER\"}",42);
        Check.That(a==b && a.Contains("\"n\":1"),"VM globals, closures or random state leaked between requests");
        Check.That(worker.Invoke("return {decide=function(c) return secret==nil and math.private==nil end}","other-seat.lua","decide","{}")=="true");
        Check.That(worker.ProcessId==first,"Successful calls did not reuse the same worker");
        var replies=Task.WhenAll(Enumerable.Range(0,8).Select(i=>Task.Run(()=>worker.Invoke("return {decide=function(c) return c.id end}","concurrent.lua","decide","{\"id\":"+i+"}")))).GetAwaiter().GetResult();
        Check.That(replies.SequenceEqual(Enumerable.Range(0,8).Select(i=>i.ToString())),"Concurrent replies were mixed");
        Check.Throws(()=>worker.Invoke("return {decide=function(c) error('expected-worker-error') end}","error.lua","decide","{}"),"expected-worker-error");
        Check.That(worker.ProcessId==null); AssertExited(first);
        Check.That(worker.Invoke(Constant,"recovery.lua","decide","{}")=="7");
        var recovered=worker.ProcessId!.Value;Check.That(recovered!=first);
        Check.Throws(()=>worker.Invoke("return {decide=function(c) return string.rep('x',1000000000) end}","memory.lua","decide","{}"),"memory.lua");
        Check.That(worker.ProcessId==null);AssertExited(recovered);
        Check.That(worker.Invoke(Constant,"recovery.lua","decide","{}")=="7");
        var disposed=worker.ProcessId!.Value;worker.Dispose();worker.Dispose();AssertExited(disposed);
        Check.Throws(()=>worker.Invoke(Constant,"disposed.lua","decide","{}"),"");

        // Expensive native calls outrun the instruction budget in wall time: the parent must kill them.
        using var timeout=new LuaWorkerSession();
        timeout.Invoke(Constant,"warm.lua","decide","{}");var timed=timeout.ProcessId!.Value;
        var clock=Stopwatch.StartNew();
        Check.Throws(()=>timeout.Invoke(NativeLoop,"timeout.lua","decide","{}"),"wall-time limit");
        Check.That(clock.Elapsed.TotalSeconds<8 && timeout.ProcessId==null);AssertExited(timed);
        Check.That(timeout.Invoke(Constant,"after-timeout.lua","decide","{}")=="7");

        using var canceled=new LuaWorkerSession();canceled.Invoke(Constant,"warm.lua","decide","{}");
        var canceledPid=canceled.ProcessId!.Value;
        var call=Task.Run(()=>Check.Throws(()=>canceled.Invoke(NativeLoop,"stop.lua","decide","{}"),"stop.lua"));
        Thread.Sleep(100);clock.Restart();canceled.Dispose();
        Check.That(call.Wait(1500) && clock.Elapsed.TotalSeconds<2,"Stop waited for the five-second timeout");AssertExited(canceledPid);

        using var recycled=new LuaWorkerSession();
        for(var i=0;i<256;i++) Check.That(recycled.Invoke(Constant,"recycle.lua","decide","{}")=="7");
        Check.That(recycled.ProcessId==null,"Worker was not periodically recycled");
        Check.That(recycled.Invoke(Constant,"renew.lua","decide","{}")=="7");
        var crashed=recycled.ProcessId!.Value;
        using(var child=Process.GetProcessById(crashed)) { child.Kill();child.WaitForExit(); }
        Check.That(recycled.Invoke(Constant,"after-crash.lua","decide","{}")=="7" && recycled.ProcessId!=crashed);
        ProtocolLimits();
        ParentExit();
    }
    // Invoked only by the test executable in a child host, which the parent test terminates abruptly.
    public static void ParentProbe(string path)
    {
        using var worker=new LuaWorkerSession();worker.Invoke(Constant,"warm.lua","decide","{}");
        File.WriteAllText(path,worker.ProcessId!.Value.ToString());
        worker.Invoke(NativeLoop,"orphan.lua","decide","{}");
    }
    private static void ParentExit()
    {
        var path=Path.GetFullPath(Path.Combine("artifacts","worker-parent-"+Guid.NewGuid()+".txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true };
        if(Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(WorkerTests).Assembly.Location);
        start.ArgumentList.Add("--worker-parent-probe");start.ArgumentList.Add(path);
        using var parent=Process.Start(start)!;int? childPid=null;
        try
        {
            var clock=Stopwatch.StartNew();
            while(!File.Exists(path) && !parent.HasExited && clock.Elapsed.TotalSeconds<5) Thread.Sleep(10);
            Check.That(File.Exists(path),"Child host did not start worker");
            // The writer closes before entering the slow script; allow that tiny write to finish.
            Thread.Sleep(100);childPid=int.Parse(File.ReadAllText(path));
            parent.Kill();parent.WaitForExit();AssertExited(childPid.Value);
        }
        finally
        {
            if(!parent.HasExited) { parent.Kill(true);parent.WaitForExit(); }
            if(childPid is int pid)
            {
                try { using var child=Process.GetProcessById(pid);if(!child.HasExited) child.Kill(true); }
                catch(ArgumentException) { }
            }
            File.Delete(path);
        }
    }
    private static void AssertExited(int pid)
    {
        try { using var child=Process.GetProcessById(pid);Check.That(child.WaitForExit(1500),"Worker survived cleanup"); }
        catch(ArgumentException) { }
    }
    private static void ProtocolLimits()
    {
        foreach(var size in new[] {-1,LuaProtocol.ResponseLimit+1})
        {
            var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,size);
            Check.Throws(()=>LuaProtocol.Read(new MemoryStream(header),LuaProtocol.ResponseLimit).GetAwaiter().GetResult(),"budget");
        }
        Check.Throws(()=>LuaProtocol.Read(new MemoryStream(new byte[] {3,0,0,0,1}),10).GetAwaiter().GetResult(),"");
        Check.Throws(()=>LuaProtocol.Write(new MemoryStream(),new string('x',11),10).GetAwaiter().GetResult(),"budget");
        using var stream=new MemoryStream();
        LuaProtocol.Write(stream,"中文\nframed",100).GetAwaiter().GetResult();LuaProtocol.Write(stream,"next",100).GetAwaiter().GetResult();stream.Position=0;
        Check.That(LuaProtocol.Read(stream,100).GetAwaiter().GetResult()=="中文\nframed");
        Check.That(LuaProtocol.Read(stream,100).GetAwaiter().GetResult()=="next");
    }
}
