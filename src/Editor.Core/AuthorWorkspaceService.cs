using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed class AuthorWorkspaceService(DocumentWorkspace documents, Func<Func<object>, Task<object>> dispatch)
{
    public WorkspaceEditMode Connect(string project, string profile) => new(documents,
        new(Path.GetFullPath(project), profile, "gui", documents.WorkspaceId, documents.Generation), dispatch);
    public (string Workspace, long Generation, string? Project) Identity => (documents.WorkspaceId, documents.Generation, documents.Current?.DirectoryPath);
}
