using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class AnimationWorkspace(DocumentWorkspace documents)
{
    public const string FormKey="animation.definition";
    public ProjectIdentity Identity=>new(documents.Current?.DirectoryPath!,"editor.animation","gui",documents.WorkspaceId,documents.Generation);
    public ProjectView View()=>documents.PreviewAuthorView(Identity);
    public AnimationAuthorModel Read()=>AnimationOperations.Read(View());
    public DocumentDraft Draft()
    {var view=View();return documents.Form(FormKey,()=>view.Text(AnimationOperations.Path(view)),view.Source.Version);}
    public void Apply()
    {
        var draft=Draft();var view=View();
        if(draft.Baseline!=view.Source.Version)throw new InvalidOperationException("Animation source changed; preserve and reconcile the draft");
        var changes=AnimationOperations.Change(view,AnimationFile.Parse(draft.Text),Guid.NewGuid().ToString("N"));
        documents.ApplyAuthorForm(FormKey,()=>
        {
            using var mode=new WorkspaceEditMode(documents,changes.Source.Identity);var result=mode.ApplyInline(changes);
            if(result.State!=EditState.Applied)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Select(d=>d.Message)));
        });
    }
    public async Task Create(string path,CancellationToken cancellation)
    {
        var view=View();await using var mode=new WorkspaceEditMode(documents,view.Source.Identity);
        var result=await mode.Apply(AnimationOperations.Create(view,path,Guid.NewGuid().ToString("N")),cancellation);
        if(result.State!=EditState.Applied)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Select(d=>d.Message)));
    }
}
