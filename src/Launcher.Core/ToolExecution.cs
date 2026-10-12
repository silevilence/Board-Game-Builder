using System.Diagnostics;

namespace BoardGame.Launcher;

public sealed class ToolExecution(InstallationRegistry registry)
{
    public Process Start(string? version,string entry,IEnumerable<string> arguments,string? workingDirectory=null,bool redirect=false)
    {
        if(entry is not ("editor" or "cli" or "player" or "debugPlayer")) throw new ArgumentException("请选择工具包声明的可执行入口。");
        using var gate=registry.Lock(); var settings=registry.Read();
        version=version==null?settings.DefaultVersion:ProductManifest.NormalizeVersion(version);
        if(version==null) throw new InvalidOperationException("尚未选择默认版本，请先选择已安装版本。");
        var item=settings.Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("指定版本未安装。");
        var status=registry.Inspect(item); if(status.State!="available") throw new InvalidOperationException(status.Diagnostic);
        var manifest=ProductManifest.Read(Path.Combine(item.Directory,"toolkit.json")); _=RuntimePolicy.Resolve(manifest,item.Directory,entry);
        var start=new ProcessStartInfo(ProductManifest.SafePath(item.Directory,manifest.Json.GetProperty("entries").GetProperty(entry).GetString()!))
        {UseShellExecute=entry=="editor"&&!redirect,CreateNoWindow=true,RedirectStandardInput=redirect,RedirectStandardOutput=redirect,RedirectStandardError=redirect,WorkingDirectory=Path.GetFullPath(workingDirectory??Environment.CurrentDirectory)};
        foreach(var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)??throw new IOException("无法启动所选工具。");
    }
    public async Task<int> Run(string? version,string entry,IEnumerable<string> arguments,CancellationToken cancellation=default,string? workingDirectory=null)
    {
        cancellation.ThrowIfCancellationRequested(); using var process=Start(version,entry,arguments,workingDirectory,true);
        using var inputLifetime=new CancellationTokenSource();
        _=Task.Run(async ()=>
        {
            try {await Console.OpenStandardInput().CopyToAsync(process.StandardInput.BaseStream,inputLifetime.Token);process.StandardInput.Close();}
            catch(Exception e) when(e is IOException or OperationCanceledException or ObjectDisposedException) { }
        });
        var output=process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var errors=process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        try {await process.WaitForExitAsync(cancellation);await Task.WhenAll(output,errors);return process.ExitCode;}
        catch(OperationCanceledException)
        {
            try {if(!process.HasExited) process.Kill(true);} catch(InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);await Task.WhenAll(output,errors);return 130;
        }
        finally {inputLifetime.Cancel();}
    }
}
