using System.Buffers.Binary;
using System.Globalization;

namespace BoardGame.Kernel;

// A resource key optionally followed by a pixel rectangle: resource#x,y,width,height.
// The bytes remain a single resource throughout loading, recording and rendering.
public sealed record ImageReference(string Resource,int X=0,int Y=0,int Width=0,int Height=0)
{
    public bool IsRegion=>Width>0;
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
