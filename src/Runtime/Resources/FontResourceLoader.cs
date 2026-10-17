using System.Buffers.Binary;
using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

// One explicitly selected desktop typeface per resource pool; no system-font lookup in the loader.
internal sealed class FontResourceLoader(FontReference font) : IResourceLoader
{
    internal static FontReference Read(JsonElement configuration)
    {
        GameRuntime.CheckObject(configuration,["path","license"],"resources.font");
        string Path(string key)
        {
            if(!configuration.TryGetProperty(key,out var value) || value.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("resources.font."+key+": required project file");
            var path=value.GetString()!;ResourceSource.CheckPath(path);return path;
        }
        var result=new FontReference(Path("path"),Path("license"));
        if(result.Resource==result.License)throw new InvalidDataException("resources.font.license: must be a separate file");
        return result;
    }
    public void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder)
    {
        Validate(input.Read(font.Resource),font.Resource);
        if(input.Read(font.License).Length==0)throw new InvalidDataException(font.License+": empty font license");
        builder.Font(font);
    }
    internal static void Validate(byte[] bytes,string path)
    {
        var data=bytes.AsSpan();
        void Fail()=>throw new InvalidDataException(path+": invalid/unsupported font; expected static OpenType TTF or CFF OTF");
        if(data.Length<12 || !(data[..4].SequenceEqual("OTTO"u8) || data[..4].SequenceEqual(new byte[]{0,1,0,0}))) {Fail();return;}
        var count=BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4,2));
        if(count==0 || count>256 || 12+count*16>data.Length) {Fail();return;}
        var tables=new HashSet<string>();
        for(var i=0;i<count;i++)
        {
            var entry=data.Slice(12+i*16,16);var tag=System.Text.Encoding.ASCII.GetString(entry[..4]);
            var offset=BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8,4));var size=BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(12,4));
            if(!tables.Add(tag) || offset<12+count*16 || (ulong)offset+size>(ulong)data.Length)Fail();
        }
        if(!tables.IsSupersetOf(new[]{"cmap","head","hhea","hmtx","maxp","name"}) || tables.Contains("fvar") || !(tables.Contains("CFF ") || tables.Contains("glyf") && tables.Contains("loca")))Fail();
    }
}
