using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Player;

/// <summary>Header hints select an installed adapter, never authorize content. The adapter still uses GameRuntime.Load.</summary>
public sealed record PlayerSource(string Path,JsonElement Header,bool Snapshot)
{
    public static PlayerSource Read(string source)
    {
        var path=System.IO.Path.GetFullPath(source);
        if(Directory.Exists(path))return new(path,ReadJson(File.OpenRead(System.IO.Path.Combine(path,"game.start.json")),1024*1024),false);
        if(System.IO.Path.GetFileName(path).Equals("game.start.json",StringComparison.OrdinalIgnoreCase))return new(path,ReadJson(File.OpenRead(path),1024*1024),false);
        if(System.IO.Path.GetExtension(path).Equals(".bgm",StringComparison.OrdinalIgnoreCase))
        {
            using var zip=ZipFile.OpenRead(path);
            var entries=zip.Entries.Where(e=>e.FullName.Equals("game.start.json",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(entries.Length!=1 || entries[0].Length>1024*1024)throw new InvalidDataException("Missing, duplicate or oversized startup entry");
            return new(path,ReadJson(entries[0].Open(),1024*1024),false);
        }
        return new(path,ReadJson(File.OpenRead(path),256*1024*1024),true);
    }
    private static JsonElement ReadJson(Stream input,int limit)
    {
        using(input)using(var output=new MemoryStream())
        {
            var buffer=new byte[81920];int count;
            while((count=input.Read(buffer))>0){if(output.Length+count>limit)throw new InvalidDataException("Player input exceeds size limit");output.Write(buffer,0,count);}
            using var json=JsonDocument.Parse(output.ToArray());return json.RootElement.Clone();
        }
    }
}
public interface IPlayerProvider
{
    bool Supports(PlayerSource source);
    Window Create(PlayerSource source,PlayerLaunch launch);
}
public sealed record PlayerContribution(RuntimePluginDefinition Plugin,string Capability);
public sealed class PlayerWindowLease : IAsyncDisposable
{
    private readonly RuntimeHost host;
    private readonly TaskCompletionSource closed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Window Window {get;}
    public Task Shutdown {get;private set;}=Task.CompletedTask;
    internal PlayerWindowLease(Window window,RuntimeHost host)
    {Window=window;this.host=host;window.Closed+=(_,_)=>{Shutdown=host.DisposeAsync().AsTask();closed.TrySetResult();};}
    public async ValueTask DisposeAsync()
    {
        var visible=Window.IsVisible;Window.Close();if(visible)await closed.Task;
        await host.DisposeAsync();await Shutdown;
    }
}

/// <summary>Approved UI contributions use CordiSharp registration and lifetime, separate from game activation.</summary>
public sealed class PlayerCatalog
{
    private readonly PlayerContribution[] contributions;
    public PlayerCatalog(IEnumerable<PlayerContribution> contributions)=>this.contributions=contributions.ToArray();
    public async Task<PlayerWindowLease> OpenAsync(string path,PlayerLaunch launch)
    {
        var source=await Task.Run(()=>PlayerSource.Read(path));
        var catalog=contributions.Select(c=>c.Plugin).ToArray();
        var startup=new RuntimeStartup(1,"player","1",catalog.Select(p=>new RuntimePluginSelection(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}))).ToList());
        var host=await RuntimeHost.StartAsync(startup,catalog,contributions.Select(c=>c.Capability));
        try
        {
            var candidates=contributions.Select(c=>host.Service<IPlayerProvider>(c.Capability)).Where(p=>p.Supports(source)).ToArray();
            if(candidates.Length!=1)throw new InvalidDataException(candidates.Length==0?"No installed Player capability supports this content":"Ambiguous Player capabilities");
            var window=candidates[0].Create(source,launch);
            return new(window,host);
        }
        catch {await host.DisposeAsync();throw;}
    }
}
