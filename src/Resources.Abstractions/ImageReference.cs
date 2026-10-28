using System.Buffers.Binary;
using System.Globalization;

namespace BoardGame.Kernel;

// Device-independent image description in a resource pool. Parse/Validate serve explicit legacy loading only.
public sealed record ImageReference(string Resource,int X=0,int Y=0,int Width=0,int Height=0,
    bool Rotated=false,int OffsetX=0,int OffsetY=0,int SourceWidth=0,int SourceHeight=0,string? Atlas=null)
{
    public bool IsRegion=>Width>0;
    public (float X,float Y,float Width,float Height) Destination(float x,float y,float width,float height)=>SourceWidth==0
        ?(x,y,width,height):(x+width*OffsetX/SourceWidth,y+height*OffsetY/SourceHeight,
            width*(Rotated?Height:Width)/SourceWidth,height*(Rotated?Width:Height)/SourceHeight);
    public static ImageReference Parse(string value)
    {
        var separator=value.IndexOf('#');
        if(separator<0) return new(value);
        var parts=value[(separator+1)..].Split(',');
        var numbers=new int[4];
        if(separator==0 || parts.Length!=4) throw new InvalidDataException("Invalid image region: "+value);
        for(var i=0;i<4;i++)
            if(!int.TryParse(parts[i],NumberStyles.None,CultureInfo.InvariantCulture,out numbers[i])) throw new InvalidDataException("Invalid image region: "+value);
        if(numbers[2]<=0 || numbers[3]<=0) throw new InvalidDataException("Empty image region: "+value);
        return new(value[..separator],numbers[0],numbers[1],numbers[2],numbers[3]);
    }
    public void CheckBounds(int width,int height)
    {
        if(IsRegion && ((long)X+Width>width || (long)Y+Height>height)) throw new InvalidDataException("Image region outside resource: "+Resource);
    }
    public void Validate(IReadOnlyDictionary<string,byte[]> resources)
    {
        if(!resources.TryGetValue(Resource,out var bytes)) throw new InvalidDataException("unknown resource: "+Resource);
        if(!IsRegion) return;
        if(bytes.Length<24 || !bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) || !bytes.AsSpan(12,4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Image regions require a PNG atlas: "+Resource);
        var width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
        var height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(width<=0 || height<=0 || (long)width*height>32_000_000) throw new InvalidDataException("Atlas dimensions exceeded: "+Resource);
        CheckBounds(width,height);
    }
}
