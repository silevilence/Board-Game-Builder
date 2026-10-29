using BoardGame.Authoring;
namespace BoardGame.Editor;

// Captures all file drafts; form inputs are parameters, never an implicit save.
public sealed class AuthorImportWorkspace(DocumentWorkspace documents)
{
    public ProjectView View()=>documents.PreviewAuthorView(new(documents.Current?.DirectoryPath!,"editor.import-print","gui",documents.WorkspaceId,documents.Generation));
    public BoardImportSource Source()
    {
        var view=View();return new(view,documents.AuthorFormats.Resolve(view) ?? throw new InvalidOperationException("当前格式没有导入能力"));
    }
    public void Apply(string form,BoardImportPreview preview)
    {
        var changes=preview.Changes(Guid.NewGuid().ToString("N"));
        documents.ApplyAuthorForm(form,()=>
        {
            using var mode=new WorkspaceEditMode(documents,preview.Source.View.Source.Identity);
            var receipt=mode.ApplyInline(changes);
            if(receipt.State!=EditState.Applied) throw new InvalidOperationException("工程已改变，请重新预览："+string.Join("; ",receipt.Diagnostics.Select(d=>d.Message)));
        });
    }
}
