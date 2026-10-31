using System.Text.Json.Nodes;
using BoardGame.Authoring;

namespace BoardGame.Editor;

internal static class PublicationEditing
{
    public static JsonObject Read(DocumentWorkspace documents)=>documents.Current?.Paths.Contains(AuthorPublication.FileName)==true
        ?AuthorPublication.Parse(documents.Draft(AuthorPublication.FileName).Text):new(){["format"]=1,["providers"]=new JsonObject()};
    public static void Edit(DocumentWorkspace documents,Action<JsonObject> edit)
    {
        var settings=Read(documents);edit(settings);
        if(documents.Current?.Paths.Contains(AuthorPublication.FileName)==true)
            documents.EditDraft(AuthorPublication.FileName,settings.ToJsonString(new(){WriteIndented=true}));
        else
        {
            var identity=new ProjectIdentity(documents.Current?.DirectoryPath!,"editor.publication","gui",documents.WorkspaceId,documents.Generation);
            var view=documents.AuthorView(identity);using var mode=new WorkspaceEditMode(documents,identity);
            var receipt=mode.ApplyInline(AuthorPublication.Change(view,settings,Guid.NewGuid().ToString("N")));
            if(receipt.State!=EditState.Applied)throw new InvalidOperationException(string.Join("; ",receipt.Diagnostics.Select(p=>p.Message)));
        }
    }
}
