using BoardGame.Authoring;
using BoardGame.Plugins;

namespace BoardGame.Editor;

public sealed class RuntimePluginManagement(PluginStore store,AuthorRuntimeCatalog catalog):IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task pending=Task.CompletedTask;
    private bool disposed;
    public string DirectoryPath=>store.DirectoryPath;
    public string PackagePath {get;set;}="";
    public PluginManifest? Selection {get;set;}
    public string Status {get;private set;}="显式导入只登记实现，不修改当前工程。";
    public IReadOnlyList<PluginManifest> Installed {get;private set;}=[];
    public bool Busy {get;private set;}
    public event Action? Changed;
    public void Refresh()
    {
        try{Installed=store.Installed();catalog.Refresh();}
        catch(Exception error){Status="安装目录校验失败："+error.Message;}
        Changed?.Invoke();
    }
    public Task Import(bool replace)
    {
        var path=PackagePath;return Run(token=>
        {
        var manifest=store.Import(path,replace,token);
        return $"已{(replace?"替换":"导入")} {manifest.Id} {manifest.Version} / {manifest.Fingerprint}。工程选择未改变。";
        });
    }
    public Task Remove(PluginManifest manifest)=>Run(_=>
    {
        store.Remove(manifest.Id,manifest.Version);return $"已移除 {manifest.Id} {manifest.Version}。原工程选择保留；再次运行前须恢复匹配实现。";
    });
    private Task Run(Func<CancellationToken,string> action)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Busy)throw new InvalidOperationException("运行插件管理操作尚未结束");
        var token=new CancellationTokenSource();cancellation=token;Busy=true;Status="正在验证并更新运行插件安装…";Changed?.Invoke();
        async Task Execute()
        {
            try{Status=await Task.Run(()=>action(token.Token),token.Token);}
            catch(OperationCanceledException){Status="操作已取消；已安装版本和工程内容保持原状。";}
            catch(Exception error){Status="操作失败："+error.Message+(error.GetBaseException()==error?"":" → "+error.GetBaseException().Message);}
            finally{Busy=false;cancellation=null;token.Dispose();Refresh();}
        }
        return pending=Execute();
    }
    public async Task CancelAndWait(){cancellation?.Cancel();await pending;}
    public async ValueTask DisposeAsync(){disposed=true;await CancelAndWait();Changed=null;}
}
