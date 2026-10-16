using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

// No mutable graph state is owned by the panel or plugin.
public sealed class FlowWorkspace(DocumentWorkspace documents)
{
    public ProjectIdentity Identity => new(documents.Current?.DirectoryPath!, "editor.flow", "gui", documents.WorkspaceId, documents.Generation);
    public ProjectView View() => documents.AuthorView(Identity);
    public FlowAuthorModel Read() => FlowOperations.Read(View());
    public async Task Apply(ProjectChanges changes, CancellationToken cancellation = default)
    {
        await using var mode = new WorkspaceEditMode(documents, changes.Source.Identity);
        var receipt = await mode.Apply(changes, cancellation);
        if (receipt.State != EditState.Applied) throw new InvalidOperationException(string.Join("; ", receipt.Diagnostics.Select(d => d.Message)));
    }
    public Task Create(string path, CancellationToken cancellation = default) => Apply(FlowDesign.Create(View(), path), cancellation);
}
