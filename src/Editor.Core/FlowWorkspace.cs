using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed record FlowGraphSnapshot(ProjectView View, FlowAuthorModel Model, FlowDesignView Design);

// No mutable graph state is owned by the panel or plugin.
public sealed class FlowWorkspace(DocumentWorkspace documents)
{
    public ProjectIdentity Identity => new(documents.Current?.DirectoryPath!, "editor.flow", "gui", documents.WorkspaceId, documents.Generation);
    public ProjectView View() => documents.AuthorView(Identity);
    public FlowAuthorModel Read() => FlowOperations.Read(View());
    public FlowGraphSnapshot Load()
    {
        var view = View(); var model = FlowOperations.Read(view);
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
}
