using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.Kernel;

namespace BoardGame.View;

public sealed record SceneCaption(string Text,float X,float Y,string? Anchor=null,int Size=13,int Color=0xFFFFFF);
public sealed record SceneCaptions(int Format,SceneCaption[] Captions)
{
    public static void Append(ResourcePool? resources,List<TableItem> items)
    {
        if(resources==null || !resources.Binaries.ContainsKey("board.presentation"))return;
        var bytes=resources.GetBinary("board.presentation");
        if(bytes.Length>64*1024)throw new InvalidDataException("board.presentation exceeds 64 KiB");
        var value=JsonSerializer.Deserialize(bytes.Span,Wire.Type<SceneCaptions>(SceneJson.Default));
        if(value is not {Format:1,Captions.Length:<=64})throw new InvalidDataException("board.presentation: invalid format/captions");
        foreach(var caption in value.Captions)
        {
            if(caption==null || string.IsNullOrWhiteSpace(caption.Text) || caption.Text.Length>512 || !float.IsFinite(caption.X) || !float.IsFinite(caption.Y)
                || Math.Abs(caption.X)>10000 || Math.Abs(caption.Y)>10000 || caption.Size is <6 or >128 || caption.Color is <0 or >0xFFFFFF)
                throw new InvalidDataException("board.presentation: invalid caption");
            var anchor=caption.Anchor==null?null:items.FirstOrDefault(item=>item.Entity==caption.Anchor);
            if(caption.Anchor!=null && anchor==null)continue; // No fallback position reveals a hidden binding.
            items.Add(new(null,"caption",caption.Text,new((anchor?.Bounds.X ?? 0)+caption.X,(anchor?.Bounds.Y ?? 0)+caption.Y,0,0),Color:caption.Color,TextSize:caption.Size));
        }
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(SceneCaptions))]
internal partial class SceneJson:JsonSerializerContext;
