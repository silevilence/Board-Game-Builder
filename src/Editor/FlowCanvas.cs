using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

// Pointer gestures preview locally; each completed gesture is one document operation.
public sealed class FlowCanvas : Control
{
    private readonly DocumentWorkspace documents;
    private readonly FlowWorkspace flow;
    private readonly FlowEditorWork work;
    private readonly Action<string> report;
    public FlowGraphSnapshot? Snapshot { get; private set; }
    private FlowGraphSnapshot? gesture;
    private FlowDesignView? preview;
    private Point origin;
    private string? dragged;
    private bool panning;
    private (string Node, string Result)? port;
    public string? ActiveNode { get; set; }
    public HashSet<string> TraceEdges { get; set; } = [];
    public HashSet<string> ProblemNodes { get; set; } = [];
    public HashSet<string> ProblemEdges { get; set; } = [];
    public FlowCanvas(DocumentWorkspace documents, FlowWorkspace flow, FlowEditorWork work, Action<string> report)
    {
        this.documents = documents; this.flow = flow; this.work = work; this.report = report;
        Focusable = true; ClipToBounds = true; MinHeight = 350;
    }
    public void Load(FlowGraphSnapshot? snapshot)
    {
        Snapshot = snapshot; gesture = null; preview = null; dragged = null; panning = false; port = null; InvalidateVisual();
    }
    private FlowDesignView Design => preview ?? Snapshot!.Design;
    private Point Screen(FlowPoint point) => new(point.X * Design.Zoom + Design.PanX, point.Y * Design.Zoom + Design.PanY);
    private FlowPoint Position(FlowNode node)
    {
        if (Design.Positions.TryGetValue(node.Id, out var point)) return point;
        var index = Snapshot!.Model.Definition.Nodes.IndexOf(node);
        return new(60 + index % 3 * 270, 60 + index / 3 * 200);
    }
    private Rect Box(FlowNode node)
    {
        var point = Screen(Position(node));
        return new(point, new Size(200 * Design.Zoom, HeightOf(node) * Design.Zoom));
    }
    private static double HeightOf(FlowNode node) => Math.Max(86, 58 + (node.Results?.Count ?? 0) * 22);
    private Point Output(FlowNode node, string result)
    {
        var box = Box(node); var index = Math.Max(0, node.Results?.IndexOf(result) ?? 0);
        return new(box.Right, box.Top + (58 + index * 22) * Design.Zoom);
    }
    private Point[] Route(FlowEdge edge)
    {
        var graph = Snapshot!.Model.Definition;
        var from = graph.Nodes.FirstOrDefault(n => n.Id == edge.From); var to = graph.Nodes.FirstOrDefault(n => n.Id == edge.To);
        if (from == null || to == null) return [];
        var a = Output(from, edge.Result); var box = Box(to); var b = new Point(box.Left, box.Top + 34 * Design.Zoom);
        if (b.X > a.X + 30) return [a, new((a.X + b.X) / 2, a.Y), new((a.X + b.X) / 2, b.Y), b];
        var y = Math.Min(Box(from).Top, box.Top) - 30 * Design.Zoom;
        return [a, new(a.X + 25 * Design.Zoom, a.Y), new(a.X + 25 * Design.Zoom, y), new(b.X - 25 * Design.Zoom, y), new(b.X - 25 * Design.Zoom, b.Y), b];
    }
    public static string KindTitle(string kind) => kind switch { FlowNodeKinds.Start => "游戏开始", FlowNodeKinds.System => "系统操作", FlowNodeKinds.Player => "玩家操作", FlowNodeKinds.End => "游戏结束", _ => "未知节点" };
    private static IBrush Accent(string kind) => Brush.Parse(kind switch { FlowNodeKinds.Start => "#59CFAC", FlowNodeKinds.System => "#88ADFF", FlowNodeKinds.Player => "#EDC47C", FlowNodeKinds.End => "#D89CDC", _ => "#AAAAAA" });
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brush.Parse("#151C29"), new Rect(Bounds.Size));
        if (Snapshot == null) return;
        var scale = Design.Zoom;
        void Text(string value, Point at, IBrush brush, double size = 12) => context.DrawText(new FormattedText(value ?? "（无效文本）", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), size * scale, brush), at);
        for (double x = Design.PanX % (32 * scale); x < Bounds.Width; x += 32 * scale)
            for (double y = Design.PanY % (32 * scale); y < Bounds.Height; y += 32 * scale)
                context.DrawEllipse(Brush.Parse("#303B4D"), null, new(x, y), 1, 1);
        foreach (var edge in Snapshot.Model.Definition.Edges)
        {
            var points = Route(edge); if (points.Length == 0) continue;
            var color = documents.Selection == ("flow.edge", edge.Id) ? Brushes.White : ProblemEdges.Contains(edge.Id) ? Brushes.OrangeRed : TraceEdges.Contains(edge.Id) ? Brushes.LimeGreen : Brush.Parse("#758AA5");
            var pen = new Pen(color, 2 * scale);
            for (var i = 1; i < points.Length; i++) context.DrawLine(pen, points[i - 1], points[i]);
            var end = points[^1]; context.DrawLine(pen, end, end + new Vector(-9 * scale, -5 * scale)); context.DrawLine(pen, end, end + new Vector(-9 * scale, 5 * scale));
            Text(edge.Result + (edge.Default ? " · 默认" : edge.Condition != null ? " · " + edge.Condition.Op : "") + $" · P{edge.Priority}", points[1] + new Vector(4, -18 * scale), color, 10);
        }
        foreach (var node in Snapshot.Model.Definition.Nodes)
        {
            var box = Box(node); var selected = documents.Selection == ("flow.node", node.Id);
            var border = ProblemNodes.Contains(node.Id) ? Brushes.OrangeRed : ActiveNode == node.Id ? Brushes.LimeGreen : selected ? Brushes.White : Accent(node.Kind);
            context.DrawRectangle(Brush.Parse("#263247"), new Pen(border, selected || ActiveNode == node.Id ? 3 : 1.5), box, 8, 8);
            Text(KindTitle(node.Kind), box.TopLeft + new Vector(12 * scale, 8 * scale), Accent(node.Kind));
            var name = Design.Names.GetValueOrDefault(node.Id, node.Id);
            Text(name.Length > 20 ? name[..20] + "…" : name, box.TopLeft + new Vector(12 * scale, 30 * scale), Brushes.White);
            if (node.Kind != FlowNodeKinds.Start) context.DrawEllipse(Brushes.White, null, new(box.Left, box.Top + 34 * scale), 5 * scale, 5 * scale);
            if (node.Kind != FlowNodeKinds.End) foreach (var result in node.Results ?? [])
            {
                var at = Output(node, result);
                context.DrawEllipse(port == (node.Id, result) ? Brushes.White : Accent(node.Kind), null, at, 5 * scale, 5 * scale);
                Text(result, new(box.Left + 14 * scale, at.Y - 7 * scale), Brushes.LightGray, 10);
            }
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (Snapshot == null) return; Focus();
        var point = e.GetPosition(this); var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsMiddleButtonPressed || buttons.IsRightButtonPressed) { Begin(null, true); return; }
        foreach (var node in Snapshot.Model.Definition.Nodes.AsEnumerable().Reverse())
        {
            if (node.Kind != FlowNodeKinds.End) foreach (var result in node.Results ?? [])
                if (Length(Output(node, result) - point) < 10) { port = (node.Id, result); report("点击目标节点左侧输入端口完成连接；Esc 取消。"); InvalidateVisual(); return; }
            var box = Box(node);
            if (port is {} source && node.Kind != FlowNodeKinds.Start && Length(new Point(box.Left, box.Top + 34 * Design.Zoom) - point) < 14)
            { port = null; var snapshot = Snapshot; work.Run(token => flow.Connect(snapshot, source.Node, source.Result, node.Id, token)); return; }
            if (box.Contains(point)) { documents.Select("flow.node", node.Id); Begin(node.Id, false); return; }
        }
        foreach (var edge in Snapshot.Model.Definition.Edges)
        {
            var route = Route(edge);
            for (var i = 1; i < route.Length; i++) if (Distance(point, route[i - 1], route[i]) < 8)
            { documents.Select("flow.edge", edge.Id); InvalidateVisual(); return; }
        }
        Begin(null, true);
        void Begin(string? id, bool pan) { gesture = Snapshot; preview = Snapshot!.Design; origin = point; dragged = id; panning = pan; e.Pointer.Capture(this); e.Handled = true; InvalidateVisual(); }
    }
    private static double Length(Point p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
    private static double Distance(Point p, Point a, Point b)
    {
        var delta = b - a; var length = delta.X * delta.X + delta.Y * delta.Y;
        if (length == 0) return Length(p - a);
        var t = Math.Clamp(((p.X - a.X) * delta.X + (p.Y - a.Y) * delta.Y) / length, 0, 1);
        return Length(p - (a + delta * t));
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (gesture == null) return;
        var delta = e.GetPosition(this) - origin;
        if (panning) preview = gesture.Design with { PanX = gesture.Design.PanX + delta.X, PanY = gesture.Design.PanY + delta.Y };
        else if (dragged != null)
        {
            preview = gesture.Design;
            var initial = Position(gesture.Model.Definition.Nodes.First(n => n.Id == dragged));
            preview = gesture.Design with { Positions = new(gesture.Design.Positions) { [dragged] = new(initial.X + delta.X / Design.Zoom, initial.Y + delta.Y / Design.Zoom) } };
        }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); var source = gesture; var design = preview;
        var moved = Length(e.GetPosition(this) - origin) > 2;
        gesture = null; preview = null; dragged = null; panning = false; e.Pointer.Capture(null);
        if (source != null && design != null && moved) work.Run(token => flow.Update(source, source.Model.Definition, design, token));
        InvalidateVisual();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e); if (Snapshot == null) return;
        var old = Snapshot.Design; var zoom = Math.Clamp(old.Zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), .2, 3); var p = e.GetPosition(this);
        var design = old with { Zoom = zoom, PanX = p.X - (p.X - old.PanX) * zoom / old.Zoom, PanY = p.Y - (p.Y - old.PanY) * zoom / old.Zoom };
        var snapshot = Snapshot; work.Run(token => flow.Update(snapshot, snapshot.Model.Definition, design, token)); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { port = null; InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.Delete && Snapshot != null && documents.Selection is {} selected && selected.Collection.StartsWith("flow."))
        { var snapshot = Snapshot; work.Run(token => flow.Delete(snapshot, selected.Collection, selected.Id, token)); e.Handled = true; }
    }
    public void Locate(string collection, string id)
    {
        documents.Select(collection, id); if (Snapshot == null) return;
        var nodeId = collection == "flow.edge" ? Snapshot.Model.Definition.Edges.FirstOrDefault(e => e.Id == id)?.From : id;
        var node = Snapshot.Model.Definition.Nodes.FirstOrDefault(n => n.Id == nodeId); if (node == null) return;
        var point = Position(node); var design = Design with { PanX = 40 - point.X * Design.Zoom, PanY = 80 - point.Y * Design.Zoom };
        // Locating a diagnostic changes only the local viewport, never semantic content.
        preview = design; InvalidateVisual();
    }
}
