using BoardGame.Authoring;
using BoardGame.Runtime;
using BoardGame.Kernel;
using System.Text.Json.Nodes;

namespace BoardGame.Editor;

public sealed record FlowGraphSnapshot(ProjectView View, FlowAuthorModel Model, FlowDesignView Design);

// No mutable graph state is owned by the panel or plugin.
public sealed class FlowWorkspace(DocumentWorkspace documents)
{
    public ProjectIdentity Identity => new(documents.Current?.DirectoryPath!, "editor.flow", "gui", documents.WorkspaceId, documents.Generation);
    public ProjectView View() => documents.AuthorView(Identity);
    public FlowAuthorModel Read() => FlowOperations.Read(View());
    public bool Supported => UnavailableReason == null;
    public string? UnavailableReason
    {
        get
        {
            try { if (documents.Current == null) return "请先打开工程。"; _ = FlowOperations.Format(documents.PreviewAuthorView(Identity)); return null; }
            catch (Exception error) when (error is not OutOfMemoryException) { return error.Message + "\n可使用运行插件配置、游戏文件或外部编辑协调面板处理；新建流程命令会显式替换规则控制器。"; }
        }
    }
    public FlowGraphSnapshot Load()
    {
        var view = documents.PreviewAuthorView(Identity); var model = FlowOperations.Read(view);
        return new(view, model, FlowDesign.ReadView(view, model.Path));
    }
    public Task Update(FlowGraphSnapshot snapshot, FlowDefinition graph, FlowDesignView? design = null, CancellationToken cancellation = default) =>
        Apply(FlowDesign.Change(snapshot.View, graph, design), cancellation);
    public Task Add(FlowGraphSnapshot snapshot, string kind, double x, double y, CancellationToken cancellation = default)
    {
        if (kind is not ("start" or "system" or "player" or "end")) throw new ArgumentException("Unknown node kind");
        var id = kind + "-" + Guid.NewGuid().ToString("N")[..8];
        var node = new FlowNode { Id = id, Kind = kind, Seat = kind == "player" ? new(Current: true) : null, Winners = kind == "end" ? [] : null };
        var design = snapshot.Design with { Positions = new(snapshot.Design.Positions) { [id] = new(x, y) } };
        return Update(snapshot, snapshot.Model.Definition with { Nodes = [..snapshot.Model.Definition.Nodes, node] }, design, cancellation);
    }
    public Task Delete(FlowGraphSnapshot snapshot, string collection, string id, CancellationToken cancellation = default)
    {
        var graph = snapshot.Model.Definition;
        var design = snapshot.Design with { Positions = new(snapshot.Design.Positions), Names = new(snapshot.Design.Names) };
        if (collection == "flow.node") { design.Positions.Remove(id); design.Names.Remove(id); }
        return Update(snapshot, graph with {
            Nodes = graph.Nodes.Where(n => collection != "flow.node" || n.Id != id).ToList(),
            Edges = graph.Edges.Where(e => collection == "flow.node" ? e.From != id && e.To != id : e.Id != id).ToList()
        }, design, cancellation);
    }
    public Task Connect(FlowGraphSnapshot snapshot, string from, string result, string to, CancellationToken cancellation = default) =>
        Update(snapshot, snapshot.Model.Definition with { Edges = [..snapshot.Model.Definition.Edges, new("edge-" + Guid.NewGuid().ToString("N")[..8], from, to, result)] }, cancellation: cancellation);
    public async Task Apply(ProjectChanges changes, CancellationToken cancellation = default)
    {
        await using var mode = new WorkspaceEditMode(documents, changes.Source.Identity);
        var receipt = await mode.Apply(changes, cancellation);
        if (receipt.State != EditState.Applied) throw new InvalidOperationException(string.Join("; ", receipt.Diagnostics.Select(d => d.Message)));
    }
    public Task Create(string path, CancellationToken cancellation = default) => Apply(FlowDesign.Create(View(), path), cancellation);
    public string FormKey(string collection, string id) => $"flow.properties/{collection}/{id}";
    public string NameKey(string id) => "flow.name/" + id;
    public string ScriptKey(string id) => "flow.script.create/" + id;
    public DocumentDraft ScriptDraft(FlowGraphSnapshot snapshot, string id) => documents.Form(ScriptKey(id), () => Wire.Json(new[] { "", "execute" }), snapshot.View.Source.Version);
    public void ApplyScript(FlowGraphSnapshot snapshot, string id, CancellationToken cancellation = default)
    {
        var draft = ScriptDraft(snapshot, id);
        if (draft.Baseline != documents.PreviewAuthorView(Identity).Source.Version) throw new InvalidOperationException("脚本创建基线已变化；输入已保留，请协调后重试。");
        var values = Wire.Read<string[]>(draft.Text);
        if (values.Length != 2) throw new InvalidDataException("Script path and role are required");
        documents.ApplyAuthorForm(ScriptKey(id), () => CreateScript(snapshot, id, values[1], values[0], cancellation).GetAwaiter().GetResult());
    }
    public DocumentDraft Name(FlowGraphSnapshot snapshot, string id) => documents.Form(NameKey(id), () => snapshot.Design.Names.GetValueOrDefault(id, id), snapshot.View.Source.Version);
    public void ApplyName(FlowGraphSnapshot snapshot, string id)
    {
        var draft = Name(snapshot, id);
        if (draft.Baseline != documents.PreviewAuthorView(Identity).Source.Version) throw new InvalidOperationException("视图版本已改变；名称草稿已保留，请合并或放弃后重试。");
        if (draft.Text.Length > 128) throw new InvalidDataException("显示名称最多 128 字符。");
        var design = snapshot.Design with { Names = new(snapshot.Design.Names) { [id] = draft.Text } };
        var change = FlowDesign.Change(snapshot.View, snapshot.Model.Definition, design);
        documents.ApplyAuthorForm(NameKey(id), () => Apply(change).GetAwaiter().GetResult());
    }
    public DocumentDraft Properties(FlowGraphSnapshot snapshot, string collection, string id)
    {
        var graph = snapshot.Model.Definition;
        var json = collection == "flow.node" ? Wire.Json(graph.Nodes.First(n => n.Id == id)) : Wire.Json(graph.Edges.First(e => e.Id == id));
        return documents.Form(FormKey(collection, id), () => JsonNode.Parse(json)!.ToJsonString(new() { WriteIndented = true }), snapshot.View.Source.Version);
    }
    public void ApplyProperties(FlowGraphSnapshot snapshot, string collection, string id)
    {
        var key = FormKey(collection, id); var draft = Properties(snapshot, collection, id);
        if (draft.Baseline != documents.PreviewAuthorView(Identity).Source.Version) throw new InvalidOperationException("流程或文件版本已改变；属性草稿已保留，请合并或放弃后重试。");
        var graph = snapshot.Model.Definition;
        if (collection == "flow.node")
        {
            var value = Wire.Read<FlowNode>(draft.Text);
            if (value.Id != id) throw new InvalidDataException("Stable node ID cannot be changed");
            graph = graph with { Nodes = graph.Nodes.Select(n => n.Id == id ? value : n).ToList() };
        }
        else
        {
            var value = Wire.Read<FlowEdge>(draft.Text);
            if (value.Id != id) throw new InvalidDataException("Stable edge ID cannot be changed");
            graph = graph with { Edges = graph.Edges.Select(e => e.Id == id ? value : e).ToList() };
        }
        var change = FlowDesign.Change(snapshot.View, graph);
        documents.ApplyAuthorForm(key, () => Apply(change).GetAwaiter().GetResult());
    }
    public Task CreateScript(FlowGraphSnapshot snapshot, string nodeId, string role, string path, CancellationToken cancellation = default)
    {
        if (snapshot.View.Exists(path)) throw new InvalidDataException("脚本已存在；请关联已有脚本，避免覆盖。");
        var node = snapshot.Model.Definition.Nodes.First(n => n.Id == nodeId);
        var entry = new FlowScript(path, role == "seat" ? "seat" : role == "actions" ? "actions" : role == "decide" ? "decide" : "execute");
        node = role switch { "execute" => node with { Script = entry }, "seat" => node with { Seat = new(Script: entry) },
            "actions" => node with { ActionsScript = entry, Actions = [] }, "decide" => node with { DecideScript = entry, Actions = [] }, _ => throw new ArgumentException("Unknown script role") };
        var graph = snapshot.Model.Definition with { Nodes = snapshot.Model.Definition.Nodes.Select(n => n.Id == nodeId ? node : n).ToList() };
        return Apply(FlowDesign.Change(snapshot.View, graph, scripts: new Dictionary<string, string> { [path] = FlowScriptTemplates.Text(role) }), cancellation);
    }
}
