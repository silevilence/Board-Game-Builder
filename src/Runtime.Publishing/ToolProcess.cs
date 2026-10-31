using System.Diagnostics;
using System.Text;

namespace BoardGame.Publishing;

internal sealed record ToolResult(int ExitCode,string Output);
internal static class ToolProcess
{
    internal static async Task<ToolResult> Run(string executable,IEnumerable<string> arguments,string directory,
        CancellationToken cancellation,Action<string>? line=null,IReadOnlyDictionary<string,string>? environment=null)
    {
        cancellation.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        if(environment!=null)foreach(var item in environment)start.Environment[item.Key]=item.Value;
        using var process=Process.Start(start) ?? throw new IOException("Could not start build tool: "+executable);
        using var stopped=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var output=new StringBuilder();var gate=new object();
        async Task Read(StreamReader reader)
        {
            try
            {
                while(await reader.ReadLineAsync().ConfigureAwait(false) is {} text)
                {
                    lock(gate)
                    {
                        if(output.Length>2*1024*1024)output.Remove(0,1024*1024);
                        output.AppendLine(text);line?.Invoke(text);
                    }
                }
            }
            catch{stopped.Cancel();throw;}
        }
        var standard=Read(process.StandardOutput);var errors=Read(process.StandardError);
        try {await process.WaitForExitAsync(stopped.Token).ConfigureAwait(false);}
        catch
        {
            try{if(!process.HasExited)process.Kill(entireProcessTree:true);}
            catch(InvalidOperationException)when(process.HasExited){}
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try{await Task.WhenAll(standard,errors).ConfigureAwait(false);}catch when(cancellation.IsCancellationRequested){}
            throw;
        }
        await Task.WhenAll(standard,errors).ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();
        return new(process.ExitCode,output.ToString());
    }
}
