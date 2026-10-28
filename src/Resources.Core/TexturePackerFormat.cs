using System.Text.Json;
using BoardGame.Kernel;
namespace BoardGame.Runtime;

public static class TexturePackerFormat
{
    public static IReadOnlyDictionary<string,ImageReference> Frames(JsonDocument document,string image,byte[] bytes,string location)
    {
        var frames=new Dictionary<string,ImageReference>(StringComparer.Ordinal);
        var root=document.RootElement;var meta=root.GetProperty("meta");
        var size=meta.GetProperty("size");var width=Positive(size,"w");var height=Positive(size,"h");
        var actual=ImageFile.Size(bytes,image);
        if(actual!=(width,height))throw new InvalidDataException(location+": atlas meta.size does not match image");
        foreach(var item in root.GetProperty("frames").EnumerateObject())
        {
            try
            {
                var value=item.Value;var rect=value.GetProperty("frame");var sprite=value.GetProperty("spriteSourceSize");var source=value.GetProperty("sourceSize");
                var rotated=value.GetProperty("rotated").GetBoolean();var trimmed=value.GetProperty("trimmed").GetBoolean();
                var frame=new ImageReference(image,Nonnegative(rect,"x"),Nonnegative(rect,"y"),Positive(rect,"w"),Positive(rect,"h"),
                    rotated,Nonnegative(sprite,"x"),Nonnegative(sprite,"y"),Positive(source,"w"),Positive(source,"h"));
                var sw=Positive(sprite,"w");var sh=Positive(sprite,"h");
                if(sw!=(rotated?frame.Height:frame.Width) || sh!=(rotated?frame.Width:frame.Height) ||
                   (long)frame.OffsetX+sw>frame.SourceWidth || (long)frame.OffsetY+sh>frame.SourceHeight ||
                   (long)frame.SourceWidth*frame.SourceHeight>32_000_000 ||
                   (!trimmed && (frame.OffsetX!=0 || frame.OffsetY!=0 || sw!=frame.SourceWidth || sh!=frame.SourceHeight)))
                    throw new InvalidDataException("inconsistent source/trim rectangle");
                frame.CheckBounds(width,height);
                if(string.IsNullOrWhiteSpace(item.Name) || !frames.TryAdd(item.Name,frame))
                    throw new InvalidDataException("duplicate/conflicting frame name: "+item.Name);
                if(frames.Count>16384)throw new InvalidDataException("Atlas frame budget exceeded");
            }
            catch(Exception e) when(e is not OutOfMemoryException)
            {throw new InvalidDataException(location+".frames["+item.Name+"]: "+e.Message,e);}
        }
        return frames;
    }
    public static JsonDocument Read(byte[] bytes,string location)
    {
        JsonDocument? document=null;
        try
        {
            var offset=bytes.AsSpan().StartsWith(new byte[]{239,187,191})?3:0;
            document=JsonDocument.Parse(bytes.AsMemory(offset));var root=document.RootElement;
            if(root.GetProperty("frames").ValueKind!=JsonValueKind.Object)throw new InvalidDataException("expected TexturePacker JSON Hash frames object");
            CheckDuplicates(root);var meta=root.GetProperty("meta");
            if(string.IsNullOrWhiteSpace(meta.GetProperty("image").GetString()))throw new InvalidDataException("meta.image is required");
            if(meta.TryGetProperty("related_multi_packs",out var packs) && packs.GetArrayLength()>0)throw new InvalidDataException("Declare each atlas explicitly; multi-pack links are unsupported");
            return document;
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {document?.Dispose();throw new InvalidDataException(location+": "+e.Message,e);}
    }
    private static int Positive(JsonElement value,string name)
    {var n=Nonnegative(value,name);return n>0?n:throw new InvalidDataException(name+": must be positive");}
    private static int Nonnegative(JsonElement value,string name)
    {if(!value.GetProperty(name).TryGetInt32(out var n) || n<0)throw new InvalidDataException(name+": expected nonnegative integer");return n;}
    private static void CheckDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {if(!names.Add(property.Name))throw new InvalidDataException("duplicate key: "+property.Name);CheckDuplicates(property.Value);}
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())CheckDuplicates(item);
    }
}
