using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using BoardGame.Kernel;

namespace BoardGame.Resources;

/// <summary>Device-independent bytes and image/font descriptors; no board state or GPU handles.</summary>
public class AssetSet
{
    public static AssetSet Empty {get;}=new(new Dictionary<string,ReadOnlyMemory<byte>>(),new Dictionary<string,ImageReference>(),new Dictionary<string,string>());
    [JsonPropertyOrder(0)] public IReadOnlyDictionary<string,ReadOnlyMemory<byte>> Files {get;}
    [JsonPropertyOrder(1)] public IReadOnlyDictionary<string,ImageReference> Images {get;}
    [JsonPropertyOrder(2)] public IReadOnlyDictionary<string,string> Binaries {get;}
    [JsonPropertyOrder(3),JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public FontReference? Font {get;}
    [JsonConstructor]
    public AssetSet(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> files,IReadOnlyDictionary<string,ImageReference> images,IReadOnlyDictionary<string,string> binaries,FontReference? font=null)
    {
        Files=new ReadOnlyDictionary<string,ReadOnlyMemory<byte>>(new SortedDictionary<string,ReadOnlyMemory<byte>>(files.ToDictionary(p=>p.Key,p=>(ReadOnlyMemory<byte>)p.Value.ToArray()),StringComparer.Ordinal));
        Images=new ReadOnlyDictionary<string,ImageReference>(new SortedDictionary<string,ImageReference>(images.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal));
        Binaries=new ReadOnlyDictionary<string,string>(new SortedDictionary<string,string>(binaries.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal));
        Font=font;
    }
    public ImageReference GetImage(string name)=>Images.TryGetValue(name,out var image)?image:throw new InvalidDataException("unknown image resource: "+name);
    public ReadOnlyMemory<byte> GetFile(string path)=>Files.TryGetValue(path,out var bytes)?bytes:throw new InvalidDataException("unknown resource file: "+path);
    public ReadOnlyMemory<byte> GetBinary(string name)=>Binaries.TryGetValue(name,out var path)?GetFile(path):throw new InvalidDataException("unknown binary resource: "+name);
}
