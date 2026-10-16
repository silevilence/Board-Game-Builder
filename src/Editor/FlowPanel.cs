using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed class FlowPanel : UserControl
{
    public FlowPanel(EditorPluginScope scope)
    {
        var documents = scope.Service<DocumentWorkspace>("documents");
        var flow = scope.Service<FlowWorkspace>("flow.author");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var canvas = new FlowCanvas(documents, flow, scope.Cancellation, message => status.Text = message);
        var tools = new WrapPanel();
        foreach (var kind in new[] { "start", "system", "player", "end" })
        {
            var button = new Button { Content = "+ " + FlowCanvas.KindTitle(kind) };
            button.Click += async (_, _) =>
            {
                try { if (canvas.Snapshot is {} graph) await flow.Add(graph, kind, (60 - graph.Design.PanX) / graph.Design.Zoom, (80 - graph.Design.PanY) / graph.Design.Zoom, scope.Cancellation); }
                catch (Exception error) { status.Text = error.Message; }
            };
            tools.Children.Add(button);
        }
        var help = new TextBlock { Text = "拖动节点移动 · 空白/中键平移 · 滚轮缩放 · 出口→输入连线 · Delete 删除 · 文档 Undo/Redo", TextWrapping = TextWrapping.Wrap };
        var top = new StackPanel { Children = { tools, help, status } };
        var inspector = new FlowInspector(scope, flow) { Width = 360, Margin = new Avalonia.Thickness(8, 0) };
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(inspector, Dock.Right);
        root.Children.Add(top); root.Children.Add(inspector); root.Children.Add(canvas); Content = root;
        void Refresh()
        {
            try { var graph = flow.Load(); canvas.Load(graph); tools.IsEnabled = true; var model = graph.Model; status.Text = $"流程 {model.Path} · {model.Definition.Nodes.Count} 节点 · {model.Diagnostics.Count} 个问题"; }
            catch (Exception error) { canvas.Load(null); tools.IsEnabled = false; status.Text = error.Message + "\n可使用“游戏文件”编辑；新建流程会显式切换运行控制模式。"; }
        }
        documents.SelectionChanged += canvas.InvalidateVisual; scope.Own(new Cleanup(() => documents.SelectionChanged -= canvas.InvalidateVisual));
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
