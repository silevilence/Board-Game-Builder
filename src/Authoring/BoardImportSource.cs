using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed class BoardImportSource(ProjectView view,IGameAuthorFormat format)
{
    public ProjectView View {get;}=view;
    public IGameAuthorFormat Format {get;}=format.Capabilities.Contains(GameAuthorFormats.BoardModel)?format:
        throw new AuthorOperationException("format.capability","This import requires board.author-model");
    public JsonObject Configuration=>Format.Read(View);
    public IReadOnlyList<string> Paths=>View.Paths;
}

public sealed record BoardImportPreview(BoardImportSource Source,JsonObject Configuration,Dictionary<string,byte[]> Files,List<string> Report,Dictionary<string,string>? Images=null)
{
    public ProjectChanges Changes(string request)
    {
        var view=Source.View;
        foreach(var path in Files.Keys)
            if(view.Paths.Any(p=>p.Equals(path,StringComparison.OrdinalIgnoreCase))) throw new AuthorOperationException("import.conflict","Import path already exists",path);
        var changes=Files.ToDictionary(p=>p.Key,p=>(byte[]?)p.Value);
        if(Images is {Count:>0})
        {
            using var discovery=new ProjectSnapshot(view,changes);
            var resources=GameRuntime.Load(discovery.Directory).Resources!;
            var missing=Images.Where(p=>!resources.Images.TryGetValue(p.Key,out var image) || image.Resource!=p.Value || image.IsRegion).ToDictionary(p=>p.Key,p=>p.Value);
            if(missing.Count>0) changes[GameRuntime.Entry]=Encoding.UTF8.GetBytes(ResourceOperations.AddImages(JsonNode.Parse(view.Text(GameRuntime.Entry))!.AsObject(),missing).ToJsonString());
        }
        changes[Source.Format.DefinitionPath]=Encoding.UTF8.GetBytes(Source.Format.Serialize(view,Configuration));
        using(var validation=new ProjectSnapshot(view,changes)) _=GameRuntime.Load(validation.Directory);
        return view.Change(request,changes);
    }
}

public static class BoardPrintSource
{
    public static BoardGame.Kernel.LoadedModule Load(ProjectView view)
    {
        using var snapshot=new ProjectSnapshot(view);
        return GameRuntime.Load(snapshot.Directory);
    }
}
