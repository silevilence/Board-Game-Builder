using Avalonia.Controls;
using Avalonia.Media;

namespace BoardGame.Editor;

public sealed class FlowPanel : UserControl
{
    public FlowPanel(EditorPluginScope scope)
    {
        var documents = scope.Service<DocumentWorkspace>("documents");
        var flow = scope.Service<FlowWorkspace>("flow.author");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Content = status;
        void Refresh()
        {
            try { var model = flow.Read(); status.Text = $"流程 {model.Path} · {model.Definition.Nodes.Count} 节点 · {model.Diagnostics.Count} 个问题"; }
            catch (Exception error) { status.Text = error.Message + "\n可使用“游戏文件”编辑；新建流程会显式切换运行控制模式。"; }
        }
        scope.OnDocumentChanged(Refresh); Refresh();
    }
}

public static class FlowEditorPlugin
{
    public static EditorPluginDefinition Definition => new("author.flow", 1, ["flow.author"], ["documents", "sessions", "panels", "commands", "workspace"], scope =>
    {
        var documents = scope.Service<DocumentWorkspace>("documents");
        var flow = new FlowWorkspace(documents); scope.Provide("flow.author", flow);
        var commands = scope.Service<EditorCommands>("commands");
        void Open()
        {
            var tabs = scope.Service<WorkspaceShell>("workspace").Tabs;
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Tag, "author.flow"));
        }
        commands.Register(scope, new("flow.open", "打开流程设计", () => { Open(); return Task.CompletedTask; }, () => documents.Current != null));
        commands.Register(scope, new("flow.create", "新建流程（替换 Lua/命令规则控制器）", async () =>
        {
            var view = flow.View(); var path = "flow.json"; var suffix = 1;
            while (view.Exists(path) || view.Exists(BoardGame.Authoring.FlowDesign.ViewPath(path))) path = $"flow-{suffix++}.json";
            await flow.Create(path, scope.Cancellation); Open();
        }, () => documents.Current != null));
        scope.OnDocumentChanged(commands.Refresh);
        scope.Service<PanelContributions>("panels").Register(scope, "author.flow", "流程设计", () => new FlowPanel(scope));
    });
}
