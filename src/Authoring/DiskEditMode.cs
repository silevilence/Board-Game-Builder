namespace BoardGame.Authoring;

public sealed class DiskEditMode(ProjectIdentity identity) : IProjectEditMode, IDisposable
{
    private bool disposed;
    public Task<ProjectView> Read(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        // Verify the complete capture before exposing it; operations then read only these bytes.
        var first = new ProjectView(identity, ProjectPaths.Read(identity.Directory));
        var second = new ProjectView(identity, ProjectPaths.Read(identity.Directory));
        if (first.Source.Version != second.Source.Version) throw new IOException("project.capture: files changed during capture; retry reading");
        return Task.FromResult(first);
    }
    public Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default) => throw new NotSupportedException("Disk submission is not installed yet");
    public async Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default) =>
        new(requestId, EditState.Unknown, false, (await Read(cancellation)).Source, null, [new("request.unknown", "No retained receipt; do not automatically repeat a write")], []);
    public void Dispose() => disposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
