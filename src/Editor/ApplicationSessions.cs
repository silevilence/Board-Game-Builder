using Avalonia.Threading;
using BoardGame.AppRuntime;
using BoardGame.Authoring;
using BoardGame.Runtime;
using BoardGame.Plugins;

namespace BoardGame.Editor;

/// <summary>Generic instance state belongs to the workspace, not a panel or a game format.</summary>
public sealed class ApplicationSessions : IAsyncDisposable
{
    private readonly DocumentWorkspace documents;
    private readonly PluginStore? installation;
    private readonly HashSet<string> externalCatalogs=new(StringComparer.Ordinal);
    private PluginCatalogLease? imported;
    private readonly Dictionary<string,ApplicationCatalog> catalogs=new(StringComparer.Ordinal);
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private CancellationTokenSource? starting;
    private IApplicationDebugSession? session;
    private int generation;
    private Task pending=Task.CompletedTask,shutdown=Task.CompletedTask,operations=Task.CompletedTask;
    private string? activeCatalog;
    public RuntimeHost? Instance=>(session as ApplicationDebugSession)?.Host;
    public ProjectView? SourceView {get;private set;}
    public ApplicationDebugState? State {get;private set;}
    public string Status {get;private set;}="未开始应用试玩";
    public bool Busy {get;private set;}
    public bool Active=>session!=null;
    public bool External {get;set;}
    public string Executable {get;set;}=EditorPaths.DebugPlayer;
    public int? ExternalProcessId=>(session as ExternalApplicationDebugSession)?.ProcessId;
    public event Action? Changed;
    public ApplicationSessions(DocumentWorkspace documents,PluginStore? installation=null)
    {
        this.documents=documents;this.installation=installation;documents.Replaced+=Stop;
        timer.Tick+=async(_,_)=>{if(!Busy && Active)await Send("status");};
    }
    public IDisposable Register(string id,ApplicationCatalog catalog,bool importedDefinitions=false)
    {
        if(!catalogs.TryAdd(id,catalog))throw new InvalidOperationException("Duplicate application catalog: "+id);
        if(importedDefinitions)externalCatalogs.Add(id);
        Changed?.Invoke();
        return new Cleanup(()=>{if(activeCatalog==id)Stop();catalogs.Remove(id);externalCatalogs.Remove(id);Changed?.Invoke();});
    }
    private KeyValuePair<string,ApplicationCatalog>[] Matching()
    {
        var plugins=documents.Current?.Startup["plugins"]?.AsArray();
        if(plugins==null)return [];
        var ids=plugins.Select(p=>p?["id"]?.GetValue<string>()).ToHashSet();
        var importedDefinition=installation?.Installed().Any(p=>ids.Contains(p.Id) && p.Provides.Any(c=>c.Name=="definition"))==true;
        return catalogs.Where(c=>c.Value.Content.Plugins.Any(p=>p.Provides.Contains("definition") && ids.Contains(p.Id)) ||
            importedDefinition && externalCatalogs.Contains(c.Key)).ToArray();
    }
    public bool Supported {get {try{return Matching().Length==1;}catch{return false;}}}
    public Task Start()
    {
        Stop();var current=generation;starting=new();var token=starting.Token;
        var next=StartCore(current,token);pending=Task.WhenAll(pending,next);return next;
    }
    private async Task StartCore(int current,CancellationToken cancellation)
    {
        Busy=true;Changed?.Invoke();
        PluginCatalogLease? lease=null;
        try
        {
            documents.ApplyDrafts();
            var selected=Matching();if(selected.Length!=1)throw new InvalidDataException("缺少唯一匹配的应用运行能力");
            activeCatalog=selected[0].Key;
            var doc=documents.Current ?? throw new InvalidOperationException("请打开工程");
            var source=new ProjectView(new(doc.DirectoryPath ?? "", "editor.application", "gui",documents.WorkspaceId,documents.Generation),doc.CopyFiles());
            var startup=System.Text.Json.JsonSerializer.Deserialize<BoardGame.Kernel.RuntimeStartup>(source.Text(BoardGame.AppRuntime.GameRuntime.Entry),AuthorProfiles.Json)!;
            lease=installation?.Open(startup,selected[0].Value);var catalog=lease?.Catalog ?? selected[0].Value;
            var content=await Task.Run(()=>BoardGame.AppRuntime.GameRuntime.LoadFiles(source.CopyFiles(),catalog.Content),cancellation);
            cancellation.ThrowIfCancellationRequested();
            var created=External?(IApplicationDebugSession)await ExternalApplicationDebugSession.Start(Executable,content.ExportSnapshot(),cancellation,
                installation==null?[]:["--runtime-plugins",installation.DirectoryPath]):
                await ApplicationDebugSession.Start(catalog,content.ExportSnapshot());
            if(current!=generation){await created.DisposeAsync();return;}
            session=created;imported=lease;lease=null;activeCatalog=selected[0].Key;SourceView=source;Busy=false;
            await Send("status");if(current==generation)timer.Start();
        }
        catch(Exception error){if(current==generation){Busy=false;Status="应用试玩故障："+error.Message;Changed?.Invoke();}}
        finally{lease?.Dispose();}
    }
    public Task Send(string operation)
    {
        if(session==null || Busy && operation=="status")return Task.CompletedTask;
        var previous=operations;var current=generation;
        async Task Execute(){await previous;if(current==generation && session!=null)await SendCore(operation);}
        return operations=Execute();
    }
    private async Task SendCore(string operation)
    {
        var active=session;var current=generation;Busy=true;
        try
        {
            var state=await active!.SendAsync(operation);
            if(current!=generation)return;
            State=state;Status=state.Fault ?? state.Status;
        }
        catch(Exception error)
        {
            if(current==generation){Stop();Status="应用试玩故障："+error.Message;Changed?.Invoke();}
        }
        finally{if(current==generation){Busy=false;Changed?.Invoke();}}
    }
    public void Stop()
    {
        generation++;timer.Stop();starting?.Cancel();starting?.Dispose();starting=null;
        var previous=session;session=null;activeCatalog=null;SourceView=null;State=null;Busy=false;Status="已停止";
        var previousImports=imported;imported=null;
        if(previous!=null)shutdown=Task.WhenAll(shutdown,Release());else previousImports?.Dispose();
        async Task Release(){try{await previous!.DisposeAsync();}finally{previousImports?.Dispose();}}
        Changed?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {documents.Replaced-=Stop;Stop();await pending;await operations;await shutdown;Changed=null;catalogs.Clear();}
}
