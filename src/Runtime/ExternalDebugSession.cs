using System.Diagnostics;
using BoardGame.Kernel;
namespace BoardGame.Runtime;

public sealed record DebugStart(int Api,LoadedModule Module,uint Seed,Dictionary<string,string> Controllers,string Observer);
public sealed record DebugEnvelope(DebugReply? Reply,string? Error);
public sealed class DebugOperationException(string message) : Exception(message);

public sealed class ExternalDebugSession : IDebugSession
{
    private readonly Process process;
    private readonly Task<string> errors;
    private readonly SemaphoreSlim gate=new(1);
    private bool disposed;
    private ExternalDebugSession(Process process) { this.process=process; errors=process.StandardError.ReadToEndAsync(); }
    public static async Task<ExternalDebugSession> Launch(string executable,LoadedModule module,uint seed,Dictionary<string,string> controllers,string observer,bool headless=false,CancellationToken cancellation=default)
    {
        if(!File.Exists(executable)) throw new FileNotFoundException("找不到 Player，请先构建 Player 或选择官方调试发行物。",executable);
        var info=new ProcessStartInfo(Path.GetFullPath(executable)) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
        info.ArgumentList.Add("--editor-debug"); if(headless) info.ArgumentList.Add("--headless");
        var process=Process.Start(info) ?? throw new IOException("Player failed to start"); var client=new ExternalDebugSession(process);
        try
        {
            await process.StandardInput.WriteLineAsync(Wire.Json(new DebugStart(1,module,seed,controllers,observer)).AsMemory(),cancellation); await process.StandardInput.FlushAsync(cancellation);
            _=await client.Read(cancellation); return client;
        }
        catch {client.Dispose();throw;}
    }
    public async Task<DebugReply> SendAsync(DebugRequest request)
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            await process.StandardInput.WriteLineAsync(Wire.Json(request)); await process.StandardInput.FlushAsync(); return await Read();
        }
        catch(DebugOperationException) { throw; }
        catch(Exception error) { Dispose(); throw new IOException("独立 Player 调试失败："+error.Message,error); }
        finally {gate.Release();}
    }
    private async Task<DebugReply> Read(CancellationToken cancellation=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var line=await process.StandardOutput.ReadLineAsync(timeout.Token);
        if(line==null) throw new IOException("Player 已退出："+await errors);
        if(line.Length>32_000_000) throw new InvalidDataException("Debug reply too large");
        var envelope=Wire.Read<DebugEnvelope>(line);
        if(envelope.Error!=null) throw new DebugOperationException(envelope.Error);
        return envelope.Reply ?? throw new InvalidDataException("Empty debug reply");
    }
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        try { if(!process.HasExited){process.StandardInput.Close();if(!process.WaitForExit(3000))process.Kill(true);} } catch(InvalidOperationException) {} finally {process.Dispose();}
    }
}

public static class DebugTransport
{
    // Anonymous inherited stdio only: no listener, address, network or game synchronization.
    public static void Serve(Action<DebugReply,LoadedModule>? update=null,CancellationToken cancellation=default)
    {
        try
        {
            string? Read()=>Console.In.ReadLineAsync(cancellation).AsTask().GetAwaiter().GetResult();
            var line=Read() ?? throw new EndOfStreamException("Missing local debug handshake");
            if(line.Length>32_000_000) throw new InvalidDataException("Debug handshake too large");
            var start=Wire.Read<DebugStart>(line); if(start.Api!=1) throw new InvalidDataException("Debug API mismatch");
            if(start.Module.Fingerprint!=BoardGame.Modules.ModuleSnapshot.Fingerprint(start.Module)) throw new InvalidDataException("Module fingerprint mismatch");
            using var session=new DebugSession(start.Module,start.Seed,start.Controllers,start.Observer);
            Reply(session.Handle(new("frame")));
            while((line=Read())!=null)
            {
                try { if(line.Length>32_000_000) throw new InvalidDataException("Debug request too large"); Reply(session.Handle(Wire.Read<DebugRequest>(line))); }
                catch(Exception e) { Console.WriteLine(Wire.Json(new DebugEnvelope(null,e.Message))); Console.Out.Flush(); }
            }
            void Reply(DebugReply reply) { update?.Invoke(reply,start.Module); Console.WriteLine(Wire.Json(new DebugEnvelope(reply,null))); Console.Out.Flush(); }
        }
        catch(Exception e) { Console.WriteLine(Wire.Json(new DebugEnvelope(null,e.Message))); Console.Out.Flush(); }
    }
}
