using System.Buffers.Binary;

namespace BoardGame.Runtime;

internal static class ImageFile
{
    public static (int Width,int Height) Size(byte[] bytes,string path)
    {
        var data=bytes.AsSpan();int width=0,height=0;
        if(data.Length>=24 && data[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && data.Slice(12,4).SequenceEqual("IHDR"u8))
        {width=BinaryPrimitives.ReadInt32BigEndian(data.Slice(16,4));height=BinaryPrimitives.ReadInt32BigEndian(data.Slice(20,4));}
        else if(data.Length>=10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {width=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6,2));height=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8,2));}
        else if(data.Length>=26 && data[..2].SequenceEqual("BM"u8))
        {width=BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18,4));var h=BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22,4));height=h==int.MinValue?0:Math.Abs(h);}
        else if(data.Length>=4 && data[0]==255 && data[1]==216)
        {
            var i=2;
            while(i+3<data.Length)
            {
                if(data[i++]!=255)break;
                while(i<data.Length && data[i]==255)i++;
                if(i+2>=data.Length)break;var marker=data[i++];
                if(marker is 216 or 1 or >=208 and <=215)continue;
                if(marker is 217 or 218)break;
                var length=BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i,2));
                if(length<2 || i+length>data.Length)break;
                if(marker is >=192 and <=195 or >=197 and <=199 or >=201 and <=203 or >=205 and <=207)
                {if(length<7)break;height=BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i+3,2));width=BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i+5,2));break;}
                i+=length;
            }
        }
        if(width<=0 || height<=0 || (long)width*height>32_000_000)throw new InvalidDataException(path+": unsupported image or dimensions exceeded (PNG/JPEG/GIF/BMP)");
        return(width,height);
    }
}
