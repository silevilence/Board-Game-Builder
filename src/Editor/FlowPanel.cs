using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed class FlowPanel : UserControl
{
    internal FlowCanvas Canvas { get; }
    public FlowPanel(EditorPluginScope scope)
    {
        var documents = scope.Service<DocumentWorkspace>("documents");
        var flow = scope.Service<FlowWorkspace>("flow.author");
        var sessions = scope.Service<SessionController>("sessions");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var canvas = new FlowCanvas(documents, flow, scope.Cancellation, message => status.Text = message);
        Canvas = canvas;
        var tools = new WrapPanel();
        var playback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var trace = new ListBox { MaxHeight = 110 };
        var commands = scope.Service<EditorCommands>("commands");
        foreach (var id in new[] { "flow.play", "flow.stop", "flow.observe", "flow.resume" })
        {
            var command = commands.Items.First(c => c.Id == id); var button = new Button { Content = command.Title };
            button.Click += async (_, _) => { try { await commands.Execute(id); } catch (Exception error) { status.Text = error.Message; } };
            void Enabled() => button.IsEnabled = commands.CanExecute(id);
            commands.Changed += Enabled; scope.Own(new Cleanup(() => commands.Changed -= Enabled)); Enabled(); tools.Children.Add(button);
        }
        var issues = new ListBox { MaxHeight = 145 };
        var validate = new Button { Content = "校验当前草稿" };
        tools.Children.Add(validate);
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
        var top = new StackPanel { Children = { tools, help, status, playback } };
        var inspector = new FlowInspector(scope, flow) { Width = 360, Margin = new Avalonia.Thickness(8, 0) };
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(inspector, Dock.Right);
        var bottom = new StackPanel { Children = { issues, trace } }; DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(inspector); root.Children.Add(bottom); root.Children.Add(canvas); Content = root;
        BoardGame.Kernel.FlowTrace[] entries = [];
        var matching = false;
        trace.SelectionChanged += (_, _) =>
        {
            if (!matching || trace.SelectedIndex < 0 || trace.SelectedIndex >= entries.Length) return;
            var entry = entries[trace.SelectedIndex];
            canvas.Locate(entry.Edge == null ? "flow.node" : "flow.edge", entry.Edge ?? entry.Node);
        };
        void RefreshPlayback()
        {
            entries = []; trace.ItemsSource = null; canvas.ActiveNode = null; canvas.TraceEdges = []; matching = false;
            if (!sessions.Active) playback.Text = "未开始试玩。席位与 CPU 在“试玩与调试”面板配置。";
            else if (canvas.Snapshot is not {} graph || !FlowPlayback.Matches(sessions.SourceView, graph, documents.HasPendingForms))
                playback.Text = $"运行快照 {sessions.SourceView?.Source.Version[..8]} 与当前草稿不同；已关闭高亮，请显式重开。";
            else if (sessions.Reply?.AuthorityState?.Flow is not {} state)
                playback.Text = "席位投影模式。点击“显示作者轨迹”查看作者调试信息；普通玩家界面不显示这些内容。";
            else
            {
                matching = true; canvas.ActiveNode = state.Node;
                entries = state.Trace.ToArray(); canvas.TraceEdges = entries.Where(e => e.Edge != null).Select(e => e.Edge!).ToHashSet();
                trace.ItemsSource = entries.Select(e => $"{e.Revision} · {e.Node} → {e.Edge ?? "等待"} · {e.Result}").ToArray();
                playback.Text = "作者调试 · " + FlowPlayback.Status(state) + $" · 运行版本 {sessions.SourceView?.Source.Version[..8]}" +
                    (sessions.Frame?.Fault is {} fault ? $"\n{fault.Code}: {fault.Detail}（选择轨迹可定位节点与脚本）" : "");
            }
            canvas.InvalidateVisual();
        }
        BoardGame.Runtime.FlowDiagnostic[] diagnostics = [];
        issues.SelectionChanged += (_, _) =>
        {
            if (issues.SelectedIndex < 0 || issues.SelectedIndex >= diagnostics.Length) return;
            var item = diagnostics[issues.SelectedIndex];
            if (item.Edge != null) canvas.Locate("flow.edge", item.Edge); else if (item.Node != null) canvas.Locate("flow.node", item.Node);
        };
        void Refresh()
        {
            diagnostics = []; issues.ItemsSource = null;
            try
            {
                var graph = flow.Load(); canvas.Load(graph); var model = graph.Model;
                diagnostics = model.Diagnostics.ToArray();
                canvas.ProblemNodes = diagnostics.Where(d => d.Severity == "error" && d.Node != null).Select(d => d.Node!).ToHashSet();
                canvas.ProblemEdges = diagnostics.Where(d => d.Severity != "info" && d.Edge != null).Select(d => d.Edge!).ToHashSet();
                issues.ItemsSource = diagnostics.Select(d => $"[{d.Severity}] {d.Node ?? "图"} / {d.Edge} · {d.Code}: {d.Message}").ToArray();
                status.Text = $"流程 {model.Path} · {model.Definition.Nodes.Count} 节点 · {diagnostics.Count(d => d.Severity == "error")} 错误 · 版本 {model.Source.Version[..8]}" +
                    (documents.HasPendingForms ? " · 未应用字段不参与校验" : "");
            }
            catch (Exception error) { canvas.Load(null); status.Text = error.Message + "\n可使用“游戏文件”编辑；新建流程会显式切换运行控制模式。"; }
            RefreshPlayback();
        }
        validate.Click += (_, _) => Refresh();
        documents.SelectionChanged += canvas.InvalidateVisual; scope.Own(new Cleanup(() => documents.SelectionChanged -= canvas.InvalidateVisual));
        sessions.Changed += RefreshPlayback; scope.Own(new Cleanup(() => sessions.Changed -= RefreshPlayback));
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
        var sessions = scope.Service<SessionController>("sessions");
        commands.Register(scope, new("flow.play", "重开流程试玩（作者）", async () =>
        {
            var model = flow.Read(); if (!model.Success) throw new InvalidDataException("请先修复流程校验错误。");
            await sessions.Start();
            if (!scope.Cancellation.IsCancellationRequested && sessions.Active) await sessions.Send(new("observe", Authority: true));
        }, () => documents.Current != null && !documents.HasPendingForms && !sessions.Busy));
        commands.Register(scope, new("flow.stop", "停止试玩", () => { sessions.Stop(); return Task.CompletedTask; }, () => sessions.Active));
        commands.Register(scope, new("flow.observe", "显示作者轨迹", async () => { await sessions.Send(new("observe", Authority: true)); }, () => sessions.Active && !sessions.Busy));
        commands.Register(scope, new("flow.resume", "显式继续", async () => { await sessions.Send(new("resume")); }, () => sessions.Active && !sessions.Busy &&
            (sessions.Frame?.Paused == true || sessions.Frame?.Fault?.Code is "flow-budget" or "flow-cancelled" or "flow-restored")));
        sessions.Changed += commands.Refresh; scope.Own(new Cleanup(() => sessions.Changed -= commands.Refresh));
        void Open()
        {
            var tabs = scope.Service<WorkspaceShell>("workspace").Tabs;
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Tag, "author.flow"));
        }
        commands.Register(scope, new("flow.open", "打开流程设计", () => { Open(); return Task.CompletedTask; }, () => documents.Current != null));
        commands.Register(scope, new("flow.validate", "校验流程草稿", () =>
        {
            var model = flow.Read();
            scope.Service<WorkspaceShell>("workspace").Report($"版本 {model.Source.Version[..8]} · " + string.Join("; ", model.Diagnostics.Select(d => $"{d.Code}: {d.Node ?? d.Edge}")));
            Open(); return Task.CompletedTask;
        }, () => documents.Current != null && !documents.HasPendingForms));
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
