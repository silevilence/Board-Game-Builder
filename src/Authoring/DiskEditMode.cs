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
    public async Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        var current = await Read(cancellation);
        var conflicts = current.Conflicts(changes);
        if (conflicts.Length > 0) return new(changes.RequestId, EditState.Conflict, false, changes.Source, null, conflicts, []);
        var applied = new List<string>();
        try
        {
            foreach (var file in changes.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                Write(file);
                applied.Add(file.Path);
            }
            var result = await Read(cancellation);
            return new(changes.RequestId, EditState.Applied, true, changes.Source, result.Source.Version, [], applied.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return new(changes.RequestId, EditState.PartialFailure, false, changes.Source, null,
                [new("write.partial", error.Message)], applied.ToArray());
        }
    }
    private void Write(FileChange file)
    {
        var path = ProjectPaths.Resolve(identity.Directory, file.Path);
        if (file.Content == null)
        {
            if (!File.Exists(path)) { if (file.Baseline != null) throw new IOException("File removed: " + file.Path); return; }
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (ProjectView.Hash(ReadAll(input)) != file.Baseline) throw new IOException("File changed: " + file.Path);
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, file.Baseline == null ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (file.Baseline != null && ProjectView.Hash(ReadAll(stream)) != file.Baseline) throw new IOException("File changed: " + file.Path);
        stream.Position = 0; stream.Write(file.Content); stream.SetLength(file.Content.Length); stream.Flush(true);
    }
    private static byte[] ReadAll(Stream stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    public async Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default) =>
        new(requestId, EditState.Unknown, false, (await Read(cancellation)).Source, null, [new("request.unknown", "No retained receipt; do not automatically repeat a write")], []);
    public void Dispose() => disposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
