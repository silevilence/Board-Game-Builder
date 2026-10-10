using System.Text;
using System.Text.Json;
using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed partial class DocumentWorkspace
{
    public const string ExternalMergeForm = "author.external.merge";
    public ExternalStatus? ExternalMergeBaseline { get; set; }
    public ExternalStatus ExternalStatus(ProjectIdentity identity)
    {
        CheckIdentity(identity);
        var disk = ProjectPaths.Read(identity.Directory); var baseline = Current!.SavedFiles(); var workspace = Current.CopyFiles();
        foreach (var draft in drafts.Values.Where(d => d.Dirty)) workspace[draft.Path] = Encoding.UTF8.GetBytes(draft.Text);
        var changes = baseline.Keys.Union(disk.Keys, StringComparer.OrdinalIgnoreCase).Where(path => Hash(baseline, path) != Hash(disk, path))
            .Select(path => new ExternalFile(path, !baseline.ContainsKey(path) ? "added" : !disk.ContainsKey(path) ? "deleted" : "modified",
                baseline.GetValueOrDefault(path), disk.GetValueOrDefault(path), workspace.GetValueOrDefault(path))).ToArray();
        return new(identity, ProjectView.Version(disk), AuthorVersion(), Dirty, changes, forms.Where(f => f.Value.Dirty).Select(f => f.Key).ToArray());
        static string? Hash(Dictionary<string, byte[]> files, string path) => files.TryGetValue(path, out var bytes) ? ProjectView.Hash(bytes) : null;
    }
    public EditReceipt ResolveExternal(ProjectIdentity identity, string requestId, JsonElement arguments)
    {
        CheckIdentity(identity);
        var fingerprint=ProjectView.Hash(JsonSerializer.SerializeToUtf8Bytes(new {identity,arguments}));
        if(AuthorReceipts.TryGetValue(requestId,out var previous))
        {
            if(previous.Fingerprint!=fingerprint)throw new AuthorOperationException("request.mismatch","Request ID identifies another reconciliation");
            return previous.Receipt;
        }
        var status = ExternalStatus(identity);
        if (status.DiskVersion != arguments.GetProperty("diskVersion").GetString() || status.WorkspaceVersion != arguments.GetProperty("workspaceVersion").GetString())
            throw new AuthorOperationException("workspace.merge-conflict", "Disk or workspace changed while resolving; inspect both versions again");
        var choice = arguments.GetProperty("choice").GetString();
        if (choice is not ("reload" or "disk" or "workspace" or "merge")) throw new AuthorOperationException("arguments.invalid", "Unknown external resolution choice");
        if (choice == "reload" && Dirty) throw new AuthorOperationException("workspace.dirty", "Reload would discard drafts; choose an explicit resolution instead");
        var disk = new ProjectView(new(identity.Directory, "editor-merge", "disk"), ProjectPaths.Read(identity.Directory));
        if (disk.Source.Version != status.DiskVersion) throw new AuthorOperationException("workspace.merge-conflict", "Disk changed during resolution");
        var content = choice is "reload" or "disk" ? disk.CopyFiles() : choice == "merge" ? Current!.CopyFiles() : AuthorView(identity, false).CopyFiles();
        if (choice == "merge")
        {
            if (!arguments.TryGetProperty("files", out var merged)) throw new AuthorOperationException("arguments.missing", "files");
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in merged.EnumerateObject())
            {
                ProjectPaths.Validate(file.Name);
                if (!selected.Add(file.Name)) throw new AuthorOperationException("arguments.invalid", "Duplicate merged path", file.Name);
                if (file.Value.ValueKind == JsonValueKind.Null) content.Remove(file.Name);
                else if (file.Value.ValueKind == JsonValueKind.String) content[file.Name] = Encoding.UTF8.GetBytes(file.Value.GetString()!);
                else throw new AuthorOperationException("arguments.invalid", "Merged file must be UTF-8 text or null", file.Name);
            }
            if (status.Changes.Any(c => !selected.Contains(c.Path))) throw new AuthorOperationException("workspace.unresolved", "Provide a resolution for every external change");
            if (drafts.Any(d => d.Value.Dirty && !selected.Contains(d.Key.Path)) ||
                forms.Any(f => f.Key != ExternalMergeForm && f.Value.Dirty) && !selected.Contains(Current!.DefinitionPath))
                throw new AuthorOperationException("workspace.unresolved", "Provide final files for every pending file draft and the definition affected by pending form fields");
        }
        _ = disk.Paths;
        var edits = disk.Paths.Union(content.Keys).Where(path => !content.TryGetValue(path, out var value) || !disk.Exists(path) || !disk.Bytes(path).SequenceEqual(value))
            .ToDictionary(path => path, path => content.GetValueOrDefault(path));
        using var mode = new DiskEditMode(disk.Source.Identity);
        var receipt = mode.Apply(disk.Change("merge-"+ProjectView.Hash(Encoding.UTF8.GetBytes(identity.Workspace+requestId)), edits)).GetAwaiter().GetResult();
        if (receipt.State != EditState.Applied) return receipt;
        drafts.Clear(); forms.Clear(); ExternalMergeBaseline=null; authorUndo.Clear(); authorRedo.Clear(); Current!.AcceptExternal(content); Notify();
        receipt=receipt with {RequestId=requestId, Source = new(identity, status.WorkspaceVersion), ResultVersion = AuthorView(identity).Source.Version };
        AuthorReceipts.Add(requestId,(fingerprint,"external",receipt));return receipt;
    }
}
