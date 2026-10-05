using System.Diagnostics;
using System.Text;
using BoardGame.Kernel;
namespace BoardGame.Lua;
public sealed record LuaRequest(string Source,string Path,string Entry,string Input,uint Seed);
public sealed record LuaResponse(bool Success,string Json,string Error);
// A disposable worker protects the host even when a CLR library call cannot yield.
public static class LuaWorker
{
    public static string Invoke(string source,string path,string entry,string input,uint seed=1) =>
        InvokeAsync(new(source,path,entry,input,seed)).GetAwaiter().GetResult();
    private static async Task<string> InvokeAsync(LuaRequest request)
    {
        if(request.Source.Length>262144 || request.Input.Length>2097152) throw new InvalidDataException($"{request.Path}:{request.Entry}: input budget exceeded.");
        var assembly=typeof(LuaWorker).Assembly.Location;
        var bundled=Path.Combine(AppContext.BaseDirectory,"worker","Lua.exe");
        var apphost=File.Exists(bundled)?bundled:Path.ChangeExtension(assembly,OperatingSystem.IsWindows()?".exe":null);
        var hasHost=File.Exists(apphost);
        var start=new ProcessStartInfo(hasHost?apphost:"dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        if(!hasHost) start.ArgumentList.Add(assembly);
        start.Environment["DOTNET_GCHeapHardLimit"]="4000000"; // 64 MiB managed heap, hexadecimal runtime setting.
        start.Environment["DOTNET_EnableDiagnostics"]="0";
        using var process=new Process { StartInfo=start };
        try
        {
            if(!process.Start()) throw new InvalidDataException("Cannot start Lua worker.");
            var output=ReadBounded(process.StandardOutput,1048576);
            var errors=ReadBounded(process.StandardError,65536);
            var write=process.StandardInput.WriteAsync(Wire.Json(request));
            var clock=Stopwatch.StartNew();
            while(!write.IsCompleted && !process.HasExited)
            {
                if(clock.ElapsedMilliseconds>5000) throw new InvalidDataException("Lua timeout while sending input.");
                await Task.Delay(10);
            }
            await write;process.StandardInput.Close();
            while(!process.HasExited)
            {
                if(clock.ElapsedMilliseconds>5000) throw new InvalidDataException("Lua wall-time limit exceeded.");
                try
                {
                    process.Refresh();
                    if(process.PrivateMemorySize64>268435456) throw new InvalidDataException("Lua memory limit exceeded.");
                }
                catch(InvalidOperationException) when(process.HasExited) { break; }
                catch(System.ComponentModel.Win32Exception) when(process.HasExited) { break; }
                if(output.IsFaulted || errors.IsFaulted) throw new InvalidDataException("Lua output budget exceeded.");
                await Task.Delay(10);
            }
            var text=await output;var stderr=await errors;
            if(text.Length==0) throw new InvalidDataException("Lua worker failed or exceeded memory limit: " + stderr);
            var response=Wire.Read<LuaResponse>(text);
            if(!response.Success || process.ExitCode!=0) throw new InvalidDataException(response.Error);
            return response.Json;
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        { throw new InvalidDataException($"{request.Path}:{request.Entry}: {e.Message}",e); }
        finally
        {
            try { if(!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            catch(InvalidOperationException) { }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader,int limit)
    {
        var text=new StringBuilder();var buffer=new char[4096];int read;
        while((read=await reader.ReadAsync(buffer))>0)
        {
            if(text.Length+read>limit) throw new InvalidDataException("Lua output budget exceeded.");
            text.Append(buffer,0,read);
        }
        return text.ToString();
    }
}
