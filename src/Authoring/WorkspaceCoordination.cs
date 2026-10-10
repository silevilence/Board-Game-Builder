using System.Text.Json;

namespace BoardGame.Authoring;

public sealed record ExternalFile(string Path, string Kind, byte[]? Baseline, byte[]? Disk, byte[]? Workspace);
public sealed record ExternalStatus(ProjectIdentity Identity, string DiskVersion, string WorkspaceVersion, bool Dirty, ExternalFile[] Changes, string[] PendingForms);
public interface IWorkspaceCoordination
{
    Task<ExternalStatus> ExternalStatus(CancellationToken cancellation = default);
    Task<EditReceipt> ResolveExternal(string requestId, JsonElement arguments, CancellationToken cancellation = default);
}
public static class WorkspaceCoordination
{
    private static AuthorOperationInfo StatusInfo => new("workspace.external.status", "author.workspace", "Inspect external additions, edits and deletions with retained baseline, disk and workspace bytes",
        new(), new() { ["type"] = "object", ["description"] = "identity, diskVersion, workspaceVersion, dirty, changes (base64 content), pendingForms" }, ["GUI mode"], []) { ReadsProject = false };
    private static AuthorOperationInfo ResolveInfo => new("workspace.external.resolve", "author.workspace", "Explicitly coordinate external changes. reload requires a clean workspace; disk discards all workspace drafts; workspace keeps the workspace; merge supplies every changed path. Successful resolution writes the merged state to disk.",
        new() { ["diskVersion"] = new("string", true, "Disk version from status"), ["workspaceVersion"] = new("string", true, "Workspace version from status"),
            ["choice"] = new("string", true, "reload, disk, workspace or merge"), ["files"] = new("object", false, "For merge: every externally changed path and pending file draft, plus definition for pending fields, mapped to final UTF-8 text or null") },
        FileOperations.WriteInfo.ResultSchema.DeepClone().AsObject(), ["GUI mode", "Both versions still match"], ["reconciliation"]) { ReadsProject = false };
    public static AuthorPlugin Plugin => new("author.workspace", "1", 1, [], ["author.operations"], s =>
    {
        s.Register(new(StatusInfo, async call => call.Mode is IWorkspaceCoordination mode ? await mode.ExternalStatus(call.Cancellation)
            : throw new AuthorOperationException("mode.unsupported", "External coordination requires GUI mode")));
        s.Register(new(ResolveInfo, async call => call.Mode is IWorkspaceCoordination mode ? await mode.ResolveExternal(call.RequestId, call.Arguments, call.Cancellation)
            : throw new AuthorOperationException("mode.unsupported", "External coordination requires GUI mode")));
    }, "Explicit workspace/external file coordination") { Operations = [StatusInfo, ResolveInfo] };
}
