using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace BoardGame.Kernel;

// Frozen, serializable, device-independent resources for one loaded game version.
public sealed class ResourcePool
{
    public static ResourcePool Empty {get;}=new(new Dictionary<string,ReadOnlyMemory<byte>>(),new Dictionary<string,ImageReference>(),new Dictionary<string,string>());
    public IReadOnlyDictionary<string,ReadOnlyMemory<byte>> Files {get;}
    public IReadOnlyDictionary<string,ImageReference> Images {get;}
    public IReadOnlyDictionary<string,string> Binaries {get;}
    [JsonConstructor]
    public ResourcePool(IReadOnlyDictionary<string,ReadOnlyMemory<byte>> files,IReadOnlyDictionary<string,ImageReference> images,IReadOnlyDictionary<string,string> binaries)
    {
        Files=new ReadOnlyDictionary<string,ReadOnlyMemory<byte>>(new SortedDictionary<string,ReadOnlyMemory<byte>>(files.ToDictionary(p=>p.Key,p=>(ReadOnlyMemory<byte>)p.Value.ToArray()),StringComparer.Ordinal));
        Images=new ReadOnlyDictionary<string,ImageReference>(new SortedDictionary<string,ImageReference>(images.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal));
        Binaries=new ReadOnlyDictionary<string,string>(new SortedDictionary<string,string>(binaries.ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal));
    }
    public ImageReference GetImage(string name)=>Images.TryGetValue(name,out var image)?image:throw new InvalidDataException("unknown image resource: "+name);
    public ReadOnlyMemory<byte> GetFile(string path)=>Files.TryGetValue(path,out var bytes)?bytes:throw new InvalidDataException("unknown resource file: "+path);
    public ReadOnlyMemory<byte> GetBinary(string name)=>Binaries.TryGetValue(name,out var path)?GetFile(path):throw new InvalidDataException("unknown binary resource: "+name);
    public ResourcePool Select(IEnumerable<string> names)
    {
        var selected=names.Distinct(StringComparer.Ordinal).ToDictionary(n=>n,GetImage,StringComparer.Ordinal);
        var files=selected.Values.Select(v=>v.Resource).Distinct(StringComparer.Ordinal).ToDictionary(p=>p,GetFile,StringComparer.Ordinal);
        return new(files,selected,new Dictionary<string,string>());
    }
}
