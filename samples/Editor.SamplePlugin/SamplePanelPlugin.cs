using Avalonia.Controls;
using BoardGame.Editor;
using CordiSharp;
using CordiSharp.Events;
using CordiSharp.Registry;

namespace BoardGame.SamplePlugin;

[Plugin("sample.panel")]
public sealed class SamplePanelPlugin : IPlugin<EditorPluginScope>
{
    private sealed class PluginData { public int Events; }
    public void Load(Context ctx,EditorPluginScope configuration)
    {
        var scope=configuration.ForContext(ctx);
        var data=new PluginData();scope.Provide("sample.data",data);
        scope.On(EventKey.Create<int>("example.tick"),_=>data.Events++);
        scope.OnDocumentChanged(()=>data.Events++);
        var cancellation=new CancellationTokenSource();
        var task=Task.Run(async ()=> {while(true) await Task.Delay(100,cancellation.Token);});
        ctx.Effect(()=> (Func<Task>)(async ()=> {cancellation.Cancel();try {await task;} catch(OperationCanceledException) { } cancellation.Dispose();}));
        scope.Service<PanelContributions>("panels").Register(scope,"sample.panel","预编译扩展示例",()=>
        {
            // Shared Avalonia controls avoid registering collectible control types in global UI caches.
            var table=scope.Service<EditorViewports>("viewports").Create(scope);
            table.RenderingError+=_=>data.Events++;
            return new UserControl {Tag=data,Content=table};
        });
    }
}
