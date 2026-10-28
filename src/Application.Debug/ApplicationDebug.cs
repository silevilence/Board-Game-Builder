using System.Diagnostics;
using System.Text.Json;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

public sealed record ApplicationDebugStart(int Api,ContentSnapshot Content);
public sealed record ApplicationDebugState(string Status,long Tick,double SimulationMilliseconds,bool Paused,string? Fault=null,string? Error=null);
public interface IApplicationDebugSession : IAsyncDisposable
{
    Task<ApplicationDebugState> SendAsync(string operation,CancellationToken cancellation=default);
}
public sealed class ApplicationDebugSession : IApplicationDebugSession
{
    public RuntimeHost Host {get;}
    public ApplicationPump Pump {get;}
    private ApplicationDebugSession(RuntimeHost host){Host=host;Pump=new(host.Driver);}
    public static async Task<ApplicationDebugSession> Start(ApplicationCatalog catalog,ContentSnapshot snapshot)
    {
        var content=GameRuntime.Restore(snapshot,catalog.Content);
        return new(await catalog.StartAsync(content,clock:new(TimeSpan.FromSeconds(1d/60))));
    }
    public async Task<ApplicationDebugState> SendAsync(string operation,CancellationToken cancellation=default)
    {
        switch(operation)
        {
            case "pause":await Pump.SetPausedAsync(true);break;
            case "resume":await Pump.SetPausedAsync(false);break;
            case "step":await Pump.StepAsync(cancellation);break;
            case "status":break;
            default:throw new InvalidDataException("Unsupported application debug operation: "+operation);
        }
        var driver=Host.Driver;
        return new(driver.Status.ToString(),driver.CompletedTick,driver.SimulationTime.TotalMilliseconds,driver.Paused,driver.Fault?.Error.Message);
    }
    public async ValueTask DisposeAsync(){try{await Pump.DisposeAsync();}finally{await Host.DisposeAsync();}}
}

public static class ApplicationDebugProtocol
{
    public static JsonSerializerOptions Json {get;}=new(JsonSerializerDefaults.Web);
    public const int MaxLine=64*1024*1024;
    public static async Task<string> Read(TextReader reader,CancellationToken cancellation=default)
    {
        // Bound allocation while reading, not after an unbounded ReadLineAsync.
        var line=new System.Text.StringBuilder();var buffer=new char[1];
        while(true)
        {
            if(await reader.ReadAsync(buffer.AsMemory(),cancellation)==0)throw new EndOfStreamException("Application debug process disconnected");
            if(buffer[0]=='\n')return line.ToString().TrimEnd('\r');
            if(line.Length>=MaxLine)throw new InvalidDataException("Application debug message too large");
            line.Append(buffer[0]);
        }
    }
    public static async Task Serve(ApplicationCatalog catalog,Func<RuntimeHost,Task>? opened=null,CancellationToken cancellation=default)
    {
        var start=JsonSerializer.Deserialize<ApplicationDebugStart>(await Read(Console.In,cancellation).WaitAsync(cancellation),Json) ?? throw new InvalidDataException("Missing debug handshake");
        if(start.Api!=1)throw new InvalidDataException("Application debug API mismatch");
        await using var session=await ApplicationDebugSession.Start(catalog,start.Content);
        if(opened!=null)await opened(session.Host);
        await Reply(await session.SendAsync("status"));
        while(true)
        {
            string operation;
            try{operation=await Read(Console.In,cancellation).WaitAsync(cancellation);}catch(EndOfStreamException){return;}
            if(operation=="stop")return;
            try {await Reply(await session.SendAsync(operation));}
            catch(Exception error){await Reply(new("error",0,0,true,Error:error.Message));}
        }
        static Task Reply(ApplicationDebugState value)=>Console.Out.WriteLineAsync(JsonSerializer.Serialize(value,Json));
    }
}

public sealed class ExternalApplicationDebugSession : IApplicationDebugSession
{
    private readonly Process process;
    private readonly SemaphoreSlim gate=new(1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly Task<string> errors;
    private Task? stopped;
    public int ProcessId=>process.Id;
    private ExternalApplicationDebugSession(Process process){this.process=process;errors=process.StandardError.ReadToEndAsync();}
    public static async Task<ExternalApplicationDebugSession> Start(string executable,ContentSnapshot snapshot,CancellationToken cancellation=default)
    {
        if(!File.Exists(executable))throw new FileNotFoundException("Debug Player not found",executable);
        var info=new ProcessStartInfo(Path.GetFullPath(executable)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
        info.ArgumentList.Add("--editor-application-debug");
        var client=new ExternalApplicationDebugSession(Process.Start(info) ?? throw new IOException("Debug Player failed to start"));
        try
        {
            var json=JsonSerializer.Serialize(new ApplicationDebugStart(1,snapshot),ApplicationDebugProtocol.Json);
            if(json.Length>ApplicationDebugProtocol.MaxLine)throw new InvalidDataException("Application debug handshake too large");
            await client.process.StandardInput.WriteLineAsync(json.AsMemory(),cancellation);await client.process.StandardInput.FlushAsync(cancellation);
            _=await client.Read(cancellation);return client;
        }
        catch {await client.DisposeAsync();throw;}
    }
    private async Task<ApplicationDebugState> Read(CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation,lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var line=await ApplicationDebugProtocol.Read(process.StandardOutput,timeout.Token);
        var state=JsonSerializer.Deserialize<ApplicationDebugState>(line,ApplicationDebugProtocol.Json) ?? throw new IOException("Empty debug reply");
        if(state.Error!=null)throw new InvalidOperationException(state.Error);return state;
    }
    public async Task<ApplicationDebugState> SendAsync(string operation,CancellationToken cancellation=default)
    {
        if(operation is not ("status" or "pause" or "resume" or "step"))throw new InvalidDataException("Unsupported application debug operation");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,lifetime.Token);
        await gate.WaitAsync(linked.Token);
        try {await process.StandardInput.WriteLineAsync(operation.AsMemory(),linked.Token);await process.StandardInput.FlushAsync(linked.Token);return await Read(linked.Token);}
        finally {gate.Release();}
    }
    public ValueTask DisposeAsync(){lock(lifetime)return new(stopped ??=Stop());}
    private async Task Stop()
    {
        await lifetime.CancelAsync();
        await gate.WaitAsync();
        try
        {
            if(!process.HasExited)
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("stop");await process.StandardInput.FlushAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch(Exception error) when(error is IOException or TimeoutException or InvalidOperationException)
                {if(!process.HasExited)process.Kill(true);}
            }
            await process.WaitForExitAsync();await errors;
        }
        finally {process.Dispose();gate.Dispose();lifetime.Dispose();}
    }
}
