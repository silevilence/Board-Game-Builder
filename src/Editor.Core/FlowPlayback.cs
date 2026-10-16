using BoardGame.Authoring;
using BoardGame.Kernel;

namespace BoardGame.Editor;

public static class FlowPlayback
{
    public static bool Matches(ProjectView? running, FlowGraphSnapshot draft, bool pendingFields)
    {
        if (running == null || pendingFields || running.Source.Identity.Workspace != draft.View.Source.Identity.Workspace ||
            running.Source.Identity.Generation != draft.View.Source.Identity.Generation) return false;
        var path = FlowDesign.ViewPath(draft.Model.Path);
        string Version(ProjectView view) => ProjectView.Version(view.CopyFiles().Where(p => p.Key != path).ToDictionary(p => p.Key, p => p.Value));
        return Version(running) == Version(draft.View);
    }
    public static string Status(FlowState state) => $"节点 {state.Node} · {state.Status} · 等待席位 {state.Seat ?? "—"} · 结果 {state.Result ?? "—"} · 出口 {state.Edge ?? "—"}";
}
