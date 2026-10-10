using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed class AuthorWorkspaceService(DocumentWorkspace documents, Func<Func<object>, Task<object>> dispatch)
{
    public async Task<object> Handle(WorkspaceRequest request, CancellationToken cancellation)
    {
        using var mode=new WorkspaceEditMode(documents,request.Identity,dispatch);
        switch(request.Method)
        {
            case "read": var view=await mode.Read(cancellation);return new WorkspaceView(view.Source,view.CopyFiles());
            case "apply": return await mode.Apply(request.Changes ?? throw new ArgumentException("Missing changes"),cancellation);
            case "result": return await mode.Result(request.RequestId,cancellation);
            case "find": return (await mode.Find(request.RequestId,request.Intent!,cancellation))!;
            case "save": return await mode.Save(request.RequestId,cancellation);
            default: throw new AuthorOperationException("connection.method","Unknown workspace method");
        }
    }
    public WorkspaceEditMode Connect(string project, string profile) => new(documents,
        new(Path.GetFullPath(project), profile, "gui", documents.WorkspaceId, documents.Generation), dispatch);
    public (string Workspace, long Generation, string? Project) Identity => (documents.WorkspaceId, documents.Generation, documents.Current?.DirectoryPath);
}
