using System.Text;
using System.Text.Json;
using BoardGame.Authoring;
using BoardGame.Modules;

namespace BoardGame.Editor;

public sealed partial class DocumentWorkspace
{
    public string WorkspaceId { get; } = Guid.NewGuid().ToString("N");
    public long Generation { get; private set; }
    internal Dictionary<string, (string Fingerprint, string? Intent, EditReceipt Receipt)> AuthorReceipts { get; } = new();
    private sealed record AuthorState(ModuleDocumentHistory Document, Dictionary<DocumentDraftTarget, DocumentDraft> Drafts);
    private sealed record AuthorEdit(AuthorState Before, AuthorState After, string BeforeVersion, string AfterVersion);
    private readonly Stack<AuthorEdit> authorUndo = [];
    private readonly Stack<AuthorEdit> authorRedo = [];
    private AuthorState AuthorStateCopy() => new(Current!.CopyHistory(), drafts.ToDictionary(p => p.Key, p => p.Value.Copy()));
    private string AuthorVersion() => ProjectView.Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        files = Current!.Revision,
        drafts = drafts.OrderBy(p => p.Key.Path).ThenBy(p => p.Key.Yaml).Select(p => new { p.Key, p.Value.Text, p.Value.Baseline }),
        forms = forms.OrderBy(p => p.Key).Select(p => new { p.Key, p.Value.Text, p.Value.Baseline })
    }));
    private void RestoreAuthor(AuthorState state)
    {
        drafts.Clear();
        Current!.RestoreHistory(state.Document);
        foreach (var entry in state.Drafts) drafts.Add(entry.Key, entry.Value.Copy());
        Notify();
    }
    private bool UndoAuthor()
    {
        if (Current == null || authorUndo.Count == 0 || HasPendingForms || AuthorVersion() != authorUndo.Peek().AfterVersion) return false;
        var edit = authorUndo.Pop(); RestoreAuthor(edit.Before); authorRedo.Push(edit); return true;
    }
    private bool RedoAuthor()
    {
        if (Current == null || authorRedo.Count == 0 || HasPendingForms || AuthorVersion() != authorRedo.Peek().BeforeVersion) return false;
        var edit = authorRedo.Pop(); RestoreAuthor(edit.After); authorUndo.Push(edit); return true;
    }
    public ProjectView AuthorView(ProjectIdentity identity, bool checkExternal = true)
    {
        CheckIdentity(identity);
        if (checkExternal && Current!.DiskChanged()) throw new AuthorOperationException("workspace.external", "External files changed; inspect workspace.external.status and explicitly reload or merge");
        if (HasPendingForms) throw new AuthorOperationException("workspace.forms", "Apply or coordinate pending form fields before external operations; fields are preserved");
        var dirty = drafts.Where(p => p.Value.Dirty).ToArray();
        if (dirty.GroupBy(p => p.Key.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new AuthorOperationException("workspace.drafts", "JSON and YAML drafts target the same file; both are preserved");
        var content = Current!.CopyFiles();
        foreach (var (target, draft) in dirty)
        {
            string serialized;
            try { serialized = draft.Serialize(); }
            catch (Exception error) { throw new AuthorOperationException("workspace.serialize", error.Message, target.Path); }
            if (!content.TryGetValue(target.Path, out var bytes) || Encoding.UTF8.GetString(bytes) != draft.Baseline && Encoding.UTF8.GetString(bytes) != serialized)
                throw new AuthorOperationException("workspace.baseline", "File and draft diverged; coordinate before continuing", target.Path);
            content[target.Path] = Encoding.UTF8.GetBytes(serialized);
        }
        return new(identity, content);
    }
    internal void CheckIdentity(ProjectIdentity identity)
    {
        if (Current == null || identity.Workspace != WorkspaceId || identity.Generation != Generation ||
            !string.Equals(Current.DirectoryPath, identity.Directory, StringComparison.OrdinalIgnoreCase))
            throw new AuthorOperationException("workspace.stale", "The selected workspace or document generation is no longer current");
    }
    public EditReceipt ApplyAuthor(ProjectChanges changes)
    {
        CheckIdentity(changes.Source.Identity);
        var fingerprint = ProjectView.Hash(JsonSerializer.SerializeToUtf8Bytes(changes, AuthorProfiles.Json));
        if (AuthorReceipts.TryGetValue(changes.RequestId, out var previous))
        {
            if (previous.Fingerprint != fingerprint) throw new AuthorOperationException("request.mismatch", "Request ID already identifies other workspace changes");
            return previous.Receipt;
        }
        var current = AuthorView(changes.Source.Identity);
        var conflicts = current.Conflicts(changes);
        if (conflicts.Length > 0) return new(changes.RequestId, EditState.Conflict, false, changes.Source, null, conflicts, []);
        var before = AuthorStateCopy(); var beforeVersion = AuthorVersion();
        var content = Current!.CopyFiles();
        try
        {
            foreach (var file in changes.Files)
            {
                var pending = drafts.SingleOrDefault(p => p.Key.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase) && p.Value.Dirty);
                if (pending.Value != null)
                {
                    if (file.Content == null) throw new AuthorOperationException("workspace.delete-draft", "Coordinate the pending draft before deleting its file", file.Path);
                    var text = Encoding.UTF8.GetString(file.Content);
                    if (pending.Key.Yaml) text = AuthorConfiguration.ToYaml(System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject());
                    pending.Value.Edit(text);
                }
                else if (file.Content == null) { content.Remove(file.Path); foreach (var key in drafts.Keys.Where(k => k.Path == file.Path).ToArray()) drafts.Remove(key); }
                else content[file.Path] = file.Content.ToArray();
            }
            Current.RestoreFiles(content);
            var result = AuthorView(changes.Source.Identity);
            authorUndo.Push(new(before, AuthorStateCopy(), beforeVersion, AuthorVersion())); authorRedo.Clear();
            var receipt = new EditReceipt(changes.RequestId, EditState.Applied, false, changes.Source, result.Source.Version, [], changes.Files.Select(f => f.Path).ToArray());
            AuthorReceipts.Add(changes.RequestId, (fingerprint, changes.Intent, receipt));
            Notify(); return receipt;
        }
        catch { RestoreAuthor(before); throw; }
    }
}

public sealed class WorkspaceEditMode(DocumentWorkspace documents, ProjectIdentity identity,
    Func<Func<object>, Task<object>>? dispatch = null) : IProjectEditMode, IRecoverableEditMode, IDisposable
{
    private bool disposed;
    private async Task<T> OnThread<T>(Func<T> action, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested();
        object Run() { ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested(); return action()!; }
        return (T)(dispatch == null ? Run() : await dispatch(Run));
    }
    public Task<ProjectView> Read(CancellationToken cancellation = default) => OnThread(() => documents.AuthorView(identity), cancellation);
    public Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default) => OnThread(() =>
    {
        if (changes.Source.Identity != identity) throw new AuthorOperationException("workspace.stale", "Request is bound to another workspace");
        return documents.ApplyAuthor(changes);
    }, cancellation);
    public Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default) => OnThread(() =>
    {
        documents.CheckIdentity(identity);
        return documents.AuthorReceipts.TryGetValue(requestId, out var result) && result.Receipt.Source.Identity == identity ? result.Receipt
            : new(requestId, EditState.Unknown, false, documents.AuthorView(identity).Source, null, [new("request.unknown", "No receipt retained in this workspace generation")], []);
    }, cancellation);
    public Task<EditReceipt?> Find(string requestId, string intent, CancellationToken cancellation = default) => OnThread<EditReceipt?>(() =>
    {
        documents.CheckIdentity(identity);
        if (!documents.AuthorReceipts.TryGetValue(requestId, out var previous)) return null;
        if (previous.Intent != intent || previous.Receipt.Source.Identity != identity) throw new AuthorOperationException("request.mismatch", "Request belongs to different content or document generation");
        return previous.Receipt;
    }, cancellation);
    public Task<EditReceipt> Recover(string requestId, CancellationToken cancellation = default) => Result(requestId, cancellation);
    public Task<EditReceipt> Save(string requestId, CancellationToken cancellation = default) => OnThread(() =>
    {
        documents.CheckIdentity(identity);
        if(documents.AuthorReceipts.TryGetValue(requestId,out var previous))
        {
            if(previous.Fingerprint!="save" || previous.Receipt.Source.Identity!=identity)throw new AuthorOperationException("request.mismatch","Request ID identifies another workspace operation");
            return previous.Receipt;
        }
        var source = documents.AuthorView(identity).Source;
        documents.Save();
        var receipt=new EditReceipt(requestId, EditState.Applied, true, source, documents.AuthorView(identity).Source.Version, [], []);
        documents.AuthorReceipts.Add(requestId,("save","save",receipt));return receipt;
    }, cancellation);
    public void Dispose() => disposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
