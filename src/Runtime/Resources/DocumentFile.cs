using System.Text;
using System.Text.RegularExpressions;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

public static partial class DocumentFile
{
    public static DocumentLibrary Parse(string text)
    {
        if(Encoding.UTF8.GetByteCount(text)>1024*1024)throw new InvalidDataException("documents: file exceeds 1 MiB");
        var library=Wire.Read<DocumentLibrary>(text);
        if(library.Format!=1 || library.Panels==null || library.Panels.Count is <1 or >8)throw new InvalidDataException("documents: expected format 1 and 1..8 panels");
        var ids=new HashSet<string>();var total=0;
        foreach(var panel in library.Panels)
        {
            if(panel==null || string.IsNullOrWhiteSpace(panel.Id) || !ids.Add(panel.Id) || string.IsNullOrWhiteSpace(panel.Title) || panel.Title.Length>120 || panel.Width is <160 or >600 || panel.Pages==null || panel.Pages.Count is <1 or >64)
                throw new InvalidDataException("documents: invalid panel");
            foreach(var page in panel.Pages)
            {
                if(page==null || string.IsNullOrWhiteSpace(page.Title) || page.Title.Length>120 || string.IsNullOrWhiteSpace(page.Image) || !double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width is <1 or >8192 || page.Height is <1 or >8192 || page.Fields==null || (total+=page.Fields.Count)>4096)
                    throw new InvalidDataException("documents: invalid page in "+panel.Id);
                foreach(var field in page.Fields)
                {
                    if(field==null || !double.IsFinite(field.X) || !double.IsFinite(field.Y) || field.X<0 || field.Y<0 || field.X>page.Width || field.Y>page.Height || !double.IsFinite(field.Size) || field.Size is <4 or >200 || field.Text==null || field.Text.Length>1024 || field.Color==null || !Color().IsMatch(field.Color))
                        throw new InvalidDataException("documents: invalid field in "+panel.Id+"/"+page.Title);
                    if(field.Binding is {} b && (string.IsNullOrWhiteSpace(b.Entity) || string.IsNullOrWhiteSpace(b.Property) || b.NonzeroText?.Length>128 || b.Values?.Count>256 || b.Values?.Any(v=>!int.TryParse(v.Key,out _) || v.Value==null || v.Value.Length>128)==true))
                        throw new InvalidDataException("documents: invalid binding in "+panel.Id+"/"+page.Title);
                }
            }
        }
        return library;
    }
    public static void ValidateReferences(DocumentLibrary? library,GameState state,ResourcePool resources)
    {
        foreach(var page in library?.Panels.SelectMany(p=>p.Pages) ?? [])
        {
            if(!resources.Images.TryGetValue(page.Image,out var image) || image.IsRegion)throw new InvalidDataException("documents: page requires a full image: "+page.Image);
            foreach(var field in page.Fields)
                if(field.Binding is {} b && (!state.Entities.TryGetValue(b.Entity,out var entity) || !entity.Properties.ContainsKey(b.Property)))
                    throw new InvalidDataException("documents: unknown binding "+b.Entity+"."+b.Property);
        }
    }
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Color();
}
internal sealed class DocumentResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,BoardGame.Resources.AssetBuilder builder)=>
        ((ResourcePoolBuilder)builder).Documents(DocumentFile.Parse(Encoding.UTF8.GetString(input.Read(source.Path))));
}
