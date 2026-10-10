using System.Text;
using System.Text.Json;

namespace BoardGame.Authoring;

public sealed class DiskEditMode(ProjectIdentity identity) : IProjectEditMode, IRecoverableEditMode, IDisposable
{
    private bool disposed;
    // Verification hooks permit deterministic failure and process-interruption tests.
    public Action<int>? BeforeWrite { get; init; }
    private sealed record Journal(string Fingerprint, ProjectChanges Changes, EditReceipt? Receipt);
    public Task<ProjectView> Read(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        var first = new ProjectView(identity, ProjectPaths.Read(identity.Directory));
        var second = new ProjectView(identity, ProjectPaths.Read(identity.Directory));
        if (first.Source.Version != second.Source.Version) throw new IOException("project.capture: files changed during capture; retry reading");
        return Task.FromResult(first);
    }
    private string Store
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ProjectPaths.Resolve(identity.Directory, "guard");
            var path = Path.Combine(identity.Directory, ".bgb-author");
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked request storage is unsupported");
            Directory.CreateDirectory(path);
            return path;
        }
    }
    private FileStream Lock() => new(SafePrivate("writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private string SafePrivate(string name)
    {
        var path = Path.Combine(Store, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked request file is unsupported");
        return path;
    }
    private static string Key(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ArgumentException("Request ID is required (maximum 128 characters)");
        return ProjectView.Hash(Encoding.UTF8.GetBytes(id));
    }
    private Journal? Load(string id)
    {
        var path = SafePrivate(Key(id) + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), AuthorProfiles.Json)
            ?? throw new InvalidDataException("request.corrupt: " + id) : null;
    }
    private void Save(Journal journal)
    {
        var path = SafePrivate(Key(journal.Changes.RequestId) + ".json");
        var temp = SafePrivate(Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, journal, AuthorProfiles.Json); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock();
        var fingerprint = ProjectView.Hash(JsonSerializer.SerializeToUtf8Bytes(changes, AuthorProfiles.Json));
        var existing = Load(changes.RequestId);
        if (existing != null)
        {
            if (existing.Fingerprint != fingerprint) throw new AuthorOperationException("request.mismatch", "This request ID already identifies different content");
            return await Inspect(existing, cancellation);
        }
        var current = await Read(cancellation);
        var conflicts = current.Conflicts(changes);
        if (changes.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Files.Length)
            throw new InvalidDataException("Duplicate change path");
        if (conflicts.Length > 0) return new(changes.RequestId, EditState.Conflict, false, changes.Source, null, conflicts, []);
        var journal = new Journal(fingerprint, changes, null);
        Save(journal); // Durable intent precedes the first project write.
        return await Continue(journal, cancellation);
    }
    private async Task<EditReceipt> Continue(Journal journal, CancellationToken cancellation)
    {
        var changes = journal.Changes;
        var applied = new List<string>();
        EditReceipt receipt;
        try
        {
            for (var index = 0; index < changes.Files.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                BeforeWrite?.Invoke(index);
                var current = await Read(cancellation);
                var adjusted = Remaining(changes, current);
                var conflicts = current.Conflicts(adjusted);
                if (conflicts.Length > 0)
                {
                    receipt = new(changes.RequestId, EditState.PartialFailure, false, changes.Source, null, conflicts, Applied(changes, current));
                    Save(journal with { Receipt = receipt }); return receipt;
                }
                var file = changes.Files[index];
                if (!Matches(current, file)) Write(file);
                applied.Add(file.Path);
            }
            var result = await Read(cancellation);
            var completed = Applied(changes, result);
            receipt = completed.Length == changes.Files.Length
                ? new(changes.RequestId, EditState.Applied, true, changes.Source, result.Source.Version, [], completed)
                : new(changes.RequestId, EditState.PartialFailure, false, changes.Source, null, [new("write.changed", "Files changed during submission")], completed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            receipt = new(changes.RequestId, EditState.PartialFailure, false, changes.Source, null,
                [new("write.partial", error.Message)], applied.ToArray());
        }
        Save(journal with { Receipt = receipt });
        return receipt;
    }
    private static bool Matches(ProjectView view, FileChange file) => file.Content == null ? !view.Exists(file.Path)
        : view.Exists(file.Path) && ProjectView.Hash(view.Bytes(file.Path)) == ProjectView.Hash(file.Content);
    private static string[] Applied(ProjectChanges changes, ProjectView view) => changes.Files.Where(f => Matches(view, f)).Select(f => f.Path).ToArray();
    private static ProjectChanges Remaining(ProjectChanges changes, ProjectView current)
    {
        var completed = changes.Files.Where(f => Matches(current, f)).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        string? Baseline(string path, string? hash) => completed.TryGetValue(path, out var file) ? file.Content == null ? null : ProjectView.Hash(file.Content) : hash;
        return changes with
        {
            Files = changes.Files.Select(f => f with { Baseline = Baseline(f.Path, f.Baseline) }).ToArray(),
            Reads = changes.Reads.Select(r => r with { Hash = Baseline(r.Path, r.Hash) }).ToArray(),
            Enumerations = changes.Enumerations.Select(e =>
            {
                var paths = e.Paths.ToHashSet(StringComparer.Ordinal);
                foreach (var file in completed.Values.Where(f => f.Path.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase)))
                { if (file.Content == null) paths.Remove(file.Path); else paths.Add(file.Path); }
                return e with { Paths = paths.Order(StringComparer.Ordinal).ToArray() };
            }).ToArray()
        };
    }
    private void Write(FileChange file)
    {
        var path = ProjectPaths.Resolve(identity.Directory, file.Path);
        if (file.Content == null)
        {
            if (!File.Exists(path)) { if (file.Baseline != null) throw new IOException("File removed: " + file.Path); return; }
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (ProjectView.Hash(ReadAll(input)) != file.Baseline) throw new IOException("File changed: " + file.Path);
            File.Delete(path); return;
        }
        var temp = SafePrivate(Guid.NewGuid().ToString("N") + ".content");
        try
        {
            using (var staged = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { staged.Write(file.Content); staged.Flush(true); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (file.Baseline == null) File.Move(temp, path, false);
            else
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (ProjectView.Hash(ReadAll(input)) != file.Baseline) throw new IOException("File changed: " + file.Path);
                File.Replace(temp, path, null);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static byte[] ReadAll(Stream stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    private async Task<EditReceipt> Inspect(Journal journal, CancellationToken cancellation)
    {
        if (journal.Changes.Source.Identity != identity) throw new AuthorOperationException("request.identity", "Request belongs to another project/profile/mode");
        if (journal.Receipt?.State == EditState.Applied) return journal.Receipt;
        var current = await Read(cancellation);
        return new(journal.Changes.RequestId, EditState.PartialFailure, false, journal.Changes.Source, null,
            [new("request.recovery", "Durable intent exists; inspect applied paths and explicitly recover. No automatic replay.")], Applied(journal.Changes, current));
    }
    public async Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock(); var journal = Load(requestId);
        return journal == null ? new(requestId, EditState.Unknown, false, (await Read(cancellation)).Source, null,
            [new("request.unknown", "No retained receipt; expired or unknown results cannot confirm execution")], []) : await Inspect(journal, cancellation);
    }
    public async Task<EditReceipt?> Find(string requestId, string intent, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock(); var journal = Load(requestId);
        if (journal == null) return null;
        if (journal.Changes.Intent != intent) throw new AuthorOperationException("request.mismatch", "Request ID was used for a different operation, parameters or source");
        return await Inspect(journal, cancellation);
    }
    public async Task<EditReceipt> Recover(string requestId, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock(); var journal = Load(requestId) ?? throw new AuthorOperationException("request.unknown", "No journal retained for this request");
        if (journal.Changes.Source.Identity != identity) throw new AuthorOperationException("request.identity", "Request belongs to another project/profile/mode");
        return journal.Receipt?.State == EditState.Applied ? journal.Receipt : await Continue(journal, cancellation);
    }
    public void Dispose() => disposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
