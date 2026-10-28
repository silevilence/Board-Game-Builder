using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using BoardGame.Resources;

namespace BoardGame.Kernel;

// Frozen, serializable, device-independent resources for one loaded game version.
public sealed class ResourcePool : AssetSet
{
    public new static ResourcePool Empty {get;}=new(new Dictionary<string,ReadOnlyMemory<byte>>(),new Dictionary<string,ImageReference>(),new Dictionary<string,string>());
    [JsonPropertyOrder(4),JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public AnimationLibrary? Animations {get;}
    [JsonPropertyOrder(5),JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public DocumentLibrary? Documents {get;}
    [JsonConstructor]
    public ResourcePool(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> files,IReadOnlyDictionary<string,ImageReference> images,IReadOnlyDictionary<string,string> binaries,FontReference? font=null,AnimationLibrary? animations=null,DocumentLibrary? documents=null)
        :base(files,images,binaries,font)
    {
        Animations=animations==null?null:animations with {Clips=Array.AsReadOnly(animations.Clips.Select(c=>c with {
            Tracks=Array.AsReadOnly(c.Tracks.Select(t=>t with {Keys=Array.AsReadOnly(t.Keys.ToArray())}).ToArray())}).ToArray())};
        Documents=documents==null?null:documents with {Panels=Array.AsReadOnly(documents.Panels.Select(p=>p with {
            Pages=Array.AsReadOnly(p.Pages.Select(page=>page with {Fields=Array.AsReadOnly(page.Fields.Select(f=>f with {
                Binding=f.Binding is {} b?b with {Values=b.Values==null?null:new ReadOnlyDictionary<string,string>(b.Values.ToDictionary())}:null}).ToArray())}).ToArray())}).ToArray())};
    }
    public ResourcePool Select(IEnumerable<string> names)
    {
        var selected=names.Concat(Animations?.Clips.SelectMany(c=>c.Tracks).Select(t=>t.BackResource).OfType<string>() ?? [])
            .Concat(Documents?.Panels.SelectMany(p=>p.Pages).Select(p=>p.Image) ?? []).Distinct(StringComparer.Ordinal).ToDictionary(n=>n,GetImage,StringComparer.Ordinal);
        var files=selected.Values.Select(v=>v.Resource).Distinct(StringComparer.Ordinal).ToDictionary(p=>p,GetFile,StringComparer.Ordinal);
        if(Font is {} font) {files[font.Resource]=GetFile(font.Resource);files[font.License]=GetFile(font.License);}
        return new(files,selected,new Dictionary<string,string>(),Font,Animations,Documents);
    }
}
