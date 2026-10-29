using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using BoardGame.Authoring;
namespace BoardGame.Editor;

// Legacy document adapter. Production panels use AuthorImportWorkspace and a guarded ProjectView.
public sealed record ImportPreview(string Revision,JsonObject Configuration,Dictionary<string,byte[]> Files,List<string> Report,Dictionary<string,string>? Images=null)
{
    public void Apply(ModuleDocument document)=>document.Import(Revision,Configuration,Files,Images);
    internal static BoardImportSource Source(ModuleDocument document)=>new(new(new(document.DirectoryPath ?? "","legacy-import","memory"),document.CopyFiles()),document.AuthorFormat ?? throw new InvalidOperationException("Author format unavailable"));
    internal static ImportPreview From(ModuleDocument document,BoardImportPreview preview)=>new(document.Revision,preview.Configuration,preview.Files,preview.Report,preview.Images);
}
[SupportedOSPlatform("windows6.1")]
public static class ImageImport
{
    public static ImportPreview Preview(ModuleDocument document,string file,string prefix,int rows=1,int columns=1,bool tablecloth=false)=>
        ImportPreview.From(document,BoardImageImport.Preview(ImportPreview.Source(document),file,prefix,rows,columns,tablecloth));
}
