using BoardGame.Editor;
using CordiSharp;
using CordiSharp.Registry;
namespace BoardGame.AsyncProbe;

// Must be rejected before initialization, not waited on during teardown.
[Plugin("probe.async")]
public sealed class NeverEndingPlugin : IAsyncPlugin<EditorPluginScope>
{
    public Task LoadAsync(Context context,EditorPluginScope config)=>Task.Delay(Timeout.Infinite);
}
