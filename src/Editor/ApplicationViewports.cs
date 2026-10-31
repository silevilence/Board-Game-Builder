using System.Text.Json;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class EditorViewports:IAsyncDisposable
{
    private GraphicsConfiguration graphics;
    private readonly GraphicsBackendPackages packages;
    private readonly DocumentWorkspace documents;
    private readonly HashSet<PlatformViewport> viewports=[];
    private GraphicsBackendLease? lease;
    private CancellationTokenSource? cancellation;
    private Task pending=Task.CompletedTask;
    public bool Busy {get;private set;}
    public string Status {get;private set;}="宿主默认 WindowsDX；工程后端变化须显式重建视口。";
    public string Backend=>graphics.Startup.Plugins.Single().Id;
    public string ActualBackends=>string.Join(", ",viewports.Select(v=>v.ActiveBackend).OfType<string>().Distinct());
    public event Action? Changed;
    public EditorViewports(DocumentWorkspace documents,GraphicsBackendPackages packages)
    {
        this.documents=documents;this.packages=packages;var backend=WindowsDxBackend.Plugin();
        graphics=new(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,BoardGame.Kernel.RuntimeConfiguration.Empty)]),[backend]);
        documents.Replaced+=Cancel;
    }
    public ApplicationViewport Create(RuntimeHost instance)=>Track(new ApplicationViewport(graphics,instance));
    public BoardGame.View.InteractiveTable CreateBoard()
    {var table=new BoardGame.View.InteractiveTable(graphics);Track(table.Surface);return table;}
    public T Track<T>(T viewport) where T:PlatformViewport
    {
        if(Busy)throw new InvalidOperationException("正在重建图形宿主");viewports.Add(viewport);
        void Notify()=>Changed?.Invoke();viewport.GraphicsChanged+=Notify;
        viewport.Disposed+=()=>{viewports.Remove(viewport);viewport.GraphicsChanged-=Notify;Notify();};return viewport;
    }
    public void RequireCurrent(RuntimeStartup startup)
    {
        var selected=startup.Plugins.Where(p=>GraphicsBackendContract.Descriptions.Any(d=>d.Id==p.Id)).ToArray();
        if(selected.Length==0 && Backend=="render.windowsdx")return; // Existing ordinary projects use the documented host default.
        if(selected.Length!=1 || selected[0].Id!=Backend || Busy)throw new InvalidOperationException("工程所选后端与当前图形宿主不一致；先显式应用后端并重建视口。");
        RuntimeHost.Validate(new(1,"graphics","1",[selected[0]]),GraphicsBackendContract.Descriptions,[RenderingPlugin.Capability],HostMode.Graphical);
        RuntimeDiscovery.CheckObject(selected[0].Configuration,[],selected[0].Id+".configuration");
    }
    public Task Apply(){if(Busy)throw new InvalidOperationException("图形重建尚未结束");return pending=ApplyCore();}
    private async Task ApplyCore()
    {
        if(Busy)throw new InvalidOperationException("图形重建尚未结束");
        Busy=true;Changed?.Invoke();using var token=new CancellationTokenSource();cancellation=token;var generation=documents.Generation;
        GraphicsBackendLease? next=null;
        try
        {
            var view=documents.PreviewAuthorView(new(documents.Current?.DirectoryPath!,"editor.graphics","gui",documents.WorkspaceId,generation));
            var startup=JsonSerializer.Deserialize<RuntimeStartup>(view.Text(BoardGame.Runtime.GameRuntime.Entry),AuthorProfiles.Json)!;
            next=packages.Open(startup);token.Token.ThrowIfCancellationRequested();
            var snapshot=viewports.ToArray();await Task.WhenAll(snapshot.Select(v=>v.SuspendGraphics()));
            await Task.Yield();
            token.Token.ThrowIfCancellationRequested();if(documents.Generation!=generation)throw new OperationCanceledException();
            lease?.Dispose();lease=next;next=null;graphics=new(lease.Startup,[lease.Definition]);
            await Task.WhenAll(snapshot.Where(viewports.Contains).Select(v=>v.ResumeGraphics(graphics)));
            token.Token.ThrowIfCancellationRequested();
            var errors=snapshot.Where(viewports.Contains).Select(v=>v.Error).OfType<string>().ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("; ",errors));
            Status=$"宿主已选择 {Backend}；图形配置来源 {view.Source.Version[..12]}。运行实例和原试玩快照保持不变；修改规则配置须显式重开试玩。";
        }
        catch(Exception error){Status="图形重建失败："+error.Message+"；未回退其他后端，工程草稿保留。";}
        finally{next?.Dispose();cancellation=null;Busy=false;Changed?.Invoke();}
    }
    private void Cancel()=>cancellation?.Cancel();
    public async Task CancelAndWait(){Cancel();await pending;}
    public async ValueTask DisposeAsync()
    {
        documents.Replaced-=Cancel;await CancelAndWait();var snapshot=viewports.ToArray();
        foreach(var viewport in snapshot)viewport.Dispose();await Task.WhenAll(snapshot.Select(v=>v.Shutdown));lease?.Dispose();Changed=null;
    }
}
