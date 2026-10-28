using System.Diagnostics;
using System.Text;
namespace BoardGame.Lua;
public sealed record LuaRequest(string Source,string Path,string Entry,string Input,uint Seed,LuaCallShape? Shape=null,
    [property:System.Text.Json.Serialization.JsonRequired] int Api=2);
public sealed record LuaResponse(bool Success,string Json,string Error);

// Serial requests reuse only the OS process; LuaEvaluator creates a fresh VM for every request.
public sealed class LuaProcessSession(string workerAssembly,string? bundledExecutable=null) : IDisposable
{
    internal static readonly ActivitySource Diagnostics=new("BoardGame.Lua");
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly object sync=new();
    private static readonly TimeSpan WallTime=TimeSpan.FromSeconds(5);
    private Process? process;
    private Task<string>? errors;
    private bool disposed;
    private int requests;
    internal int? ProcessId { get { lock(sync) return process?.Id; } }
    public async Task<string> InvokeAsync(LuaRequest request,CancellationToken cancellation=default)
    {
        if(request.Api!=2) throw new InvalidDataException("Unsupported Lua executor protocol");
        if(request.Shape is {} shape)
        {
            if((shape.ArrayFields?.Length ?? 0)>256 || (shape.ValidateExports?.Length ?? 0)>256 ||
                (shape.ArrayFields ?? []).Concat(shape.ValidateExports ?? []).Any(s=>string.IsNullOrWhiteSpace(s) || s.Length>128))
                throw new InvalidDataException("Lua binding shape budget exceeded");
            request=request with {Shape=shape with {ArrayFields=shape.ArrayFields?.ToArray(),ValidateExports=shape.ValidateExports?.ToArray()}};
        }
        using var activity=Diagnostics.StartActivity("invoke");
        activity?.SetTag("lua.path",request.Path);
        activity?.SetTag("lua.entry",request.Entry);
        if(request.Source.Length>262144 || request.Input.Length>2097152)
            throw new InvalidDataException($"{request.Path}:{request.Entry}: input budget exceeded.");
        using var requestLifetime=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellation);
        await gate.WaitAsync(requestLifetime.Token).ConfigureAwait(false);
        Process? worker=null;
        try
        {
            worker=GetProcess();
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(requestLifetime.Token);
            deadline.CancelAfter(WallTime);
            var exchange=Exchange(worker,request,deadline.Token);
            // Observe faults even if the health monitor cancels this request first.
            _=exchange.ContinueWith(t=> { _=t.Exception; },TaskContinuationOptions.OnlyOnFaulted);
            while(!exchange.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                CheckHealth(worker);
                await Task.WhenAny(exchange,Task.Delay(10,deadline.Token)).ConfigureAwait(false);
            }
            var response=await exchange.ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if(!response.Success) throw new InvalidDataException(response.Error);
            CheckHealth(worker);
            if(++requests>=256) Recycle(worker);
            return response.Json;
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {
            if(worker!=null) Recycle(worker);
            var reason=e is OperationCanceledException
                ? lifetime.IsCancellationRequested?"Lua session stopped.":"Lua wall-time limit exceeded."
                : e.Message;
            throw new InvalidDataException($"{request.Path}:{request.Entry}: {reason}",e);
        }
        finally { gate.Release(); }
    }
    private static async Task<LuaResponse> Exchange(Process worker,LuaRequest request,CancellationToken token)
    {
        await LuaProtocol.Write(worker.StandardInput.BaseStream,LuaJson.Json(request),LuaProtocol.RequestLimit,token).ConfigureAwait(false);
        var response=await LuaProtocol.Read(worker.StandardOutput.BaseStream,LuaProtocol.ResponseLimit,token).ConfigureAwait(false);
        return LuaJson.Read<LuaResponse>(response);
    }
    private Process GetProcess()
    {
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(process!=null && !process.HasExited) return process;
            process?.Dispose();
            process=null;errors=null;
            var assembly=workerAssembly;
            var apphost=bundledExecutable!=null && File.Exists(bundledExecutable)?bundledExecutable:Path.ChangeExtension(assembly,OperatingSystem.IsWindows()?".exe":null);
            var hasHost=File.Exists(apphost);
            var start=new ProcessStartInfo(hasHost?apphost:"dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
            if(!hasHost) start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--server");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.Environment["DOTNET_GCHeapHardLimit"]="4000000"; // 64 MiB managed heap, hexadecimal runtime setting.
            start.Environment["DOTNET_EnableDiagnostics"]="0";
            var child=new Process { StartInfo=start };
            try { if(!child.Start()) throw new InvalidDataException("Cannot start Lua worker."); }
            catch { child.Dispose(); throw; }
            process=child; requests=0;
            errors=ReadErrors(child.StandardError);
            _=errors.ContinueWith(t=> { _=t.Exception; },TaskContinuationOptions.OnlyOnFaulted);
            return child;
        }
    }
    private void CheckHealth(Process worker)
    {
        // Let the pipe reader drain a final structured error before reporting EOF.
        if(worker.HasExited) return;
        worker.Refresh();
        if(worker.PrivateMemorySize64>268435456) throw new InvalidDataException("Lua memory limit exceeded.");
        if(errors?.IsFaulted==true) throw new InvalidDataException("Lua output budget exceeded.");
    }
    private void Recycle(Process worker)
    {
        lock(sync)
        {
            if(!ReferenceEquals(process,worker)) return;
            process=null; errors=null;
            try
            {
                if(!worker.HasExited) worker.Kill(true);
                worker.WaitForExit();
            }
            finally { worker.Dispose(); }
        }
    }
    public void Dispose()
    {
        lock(sync)
        {
            if(disposed) return;
            disposed=true; lifetime.Cancel();
            if(process!=null) Recycle(process);
        }
    }
    private static async Task<string> ReadErrors(StreamReader reader)
    {
        var text=new StringBuilder();var buffer=new char[4096];int read;
        while((read=await reader.ReadAsync(buffer).ConfigureAwait(false))>0)
        {
            if(text.Length+read>65536) throw new InvalidDataException("Lua output budget exceeded.");
            text.Append(buffer,0,read);
        }
        return text.ToString();
    }
}
