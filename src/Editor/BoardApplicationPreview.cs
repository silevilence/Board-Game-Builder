using BoardGame.AppRuntime;
using BoardGame.Authoring;
using BoardGame.Player;
using BoardGame.Rendering;

namespace BoardGame.Editor;

public interface IBoardApplicationPreview
{
    bool Supports(ModuleDocument? document);
    bool Active {get;}
    bool Busy {get;}
    string Status {get;}
    ProjectView? SourceView {get;}
    event Action? Changed;
    Task Start();
    void Stop();
}

/// <summary>A board.sessions adapter for explicit board applications; each window holds its captured catalogue.</summary>
public sealed class BoardApplicationPreview(DocumentWorkspace documents,GraphicsBackendPackages backends):IBoardApplicationPreview,IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task pending=Task.CompletedTask,shutdown=Task.CompletedTask;
    private NativeBoardWindow? window;
    private long operation;
    public bool Active=>window!=null;
    public int Frames=>window?.Table?.RenderedFrames ?? 0;
    public string? Error=>window?.Error;
    public bool Busy {get;private set;}
    public string Status {get;private set;}="桌游应用使用独立试玩窗口；种子和控制器来自入口草稿。";
    public ProjectView? SourceView {get;private set;}
    public event Action? Changed;
    public bool Supports(ModuleDocument? document)
    {try{return document?.Startup["plugins"]?.AsArray().Any(p=>p?["id"]?.GetValue<string>()=="definition.board-state")==true;}catch{return false;}}
    public Task Start(){var previous=pending;Stop();return pending=Open(previous);}
    private async Task Open(Task previous)
    {
        var current=operation;var generation=documents.Generation;using var token=new CancellationTokenSource();cancellation=token;Busy=true;Changed?.Invoke();
        AuthorApplicationLease? lease=null;GraphicsBackendLease? graphics=null;
        try
        {
            await previous;await shutdown;token.Token.ThrowIfCancellationRequested();
            var view=documents.AuthorView(new(documents.Current!.DirectoryPath!,"editor.board.application","gui",documents.WorkspaceId,generation));
            var runtime=documents.Runtimes.Resolve(view) as IAuthorApplicationRuntime ?? throw new InvalidOperationException("Missing board application runtime");
            lease=runtime.Open(view);var catalog=lease.Catalog;
            var content=await Task.Run(()=>GameRuntime.LoadFiles(view.CopyFiles(),catalog.Content),token.Token);
            token.Token.ThrowIfCancellationRequested();if(current!=operation || generation!=documents.Generation)throw new OperationCanceledException();
            graphics=backends.Open(content.Identity.Startup);
            var created=new NativeBoardWindow(catalog,content,new(graphics.Startup,[graphics.Definition]),content.Identity.Startup.Id+" · 草稿试玩");
            var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var ownedLease=lease;var ownedGraphics=graphics;lease=null;graphics=null;
            created.Closed+=(_,_)=>
            {
                ownedLease.Dispose();ownedGraphics.Dispose();closed.TrySetResult();
                if(ReferenceEquals(window,created)){window=null;SourceView=null;Status="试玩已关闭；工程草稿未保存。";Changed?.Invoke();}
            };
            window=created;shutdown=closed.Task;SourceView=view;created.Show();await created.Ready;
            if(current!=operation || generation!=documents.Generation){created.Close();await closed.Task;return;}
            if(created.Error!=null)throw new InvalidOperationException(created.Error);
            Status="独立桌游试玩 · 草稿版本 "+view.Source.Version[..12]+"；入口中的后端、种子、控制器与发布一致。";
        }
        catch(OperationCanceledException){if(current==operation)Status="试玩已取消。";}
        catch(Exception error){if(current==operation){Status="试玩失败："+error.Message;window?.Close();}}
        finally{lease?.Dispose();graphics?.Dispose();if(ReferenceEquals(cancellation,token))cancellation=null;if(current==operation)Busy=false;Changed?.Invoke();}
    }
    public void Stop(){operation++;cancellation?.Cancel();window?.Close();SourceView=null;Busy=false;}
    public async Task StopAndWait(){Stop();await pending;await shutdown;}
    public async ValueTask DisposeAsync(){await StopAndWait();Changed=null;}
}
