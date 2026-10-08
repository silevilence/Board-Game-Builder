using System.Text.Json;
using System.Text.RegularExpressions;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

internal sealed record ResourceSource(string Path,string? Pattern=null,string? Name=null,string Prefix="")
{
    public static ResourceSource[] Read(JsonElement configuration,string plugin)
    {
        GameRuntime.CheckObject(configuration,["sources"],plugin);
        if(!configuration.TryGetProperty("sources",out var sources))return [];
        if(sources.ValueKind!=JsonValueKind.Array || sources.GetArrayLength()>128)throw new InvalidDataException(plugin+": sources must be an array of at most 128 entries");
        return sources.EnumerateArray().Select(item=>
        {
            GameRuntime.CheckObject(item,["path","pattern","name","prefix"],plugin+".sources");
            var path=item.GetProperty("path").GetString()!;CheckPath(path,true);
            var pattern=item.TryGetProperty("pattern",out var p)?p.GetString():null;
            if(pattern!=null) {CheckPath(pattern,false,true);_=Glob(pattern);}
            var name=item.TryGetProperty("name",out var n)?n.GetString():null;
            var prefix=item.TryGetProperty("prefix",out var q)?q.GetString() ?? "":"";
            if(name!=null)CheckName(name);
            if(prefix.Length>0)CheckName(prefix);
            return new ResourceSource(path,pattern,name,prefix);
        }).ToArray();
    }
    internal static void CheckName(string name)
    {if(string.IsNullOrWhiteSpace(name) || name.Length>512 || name.Any(char.IsControl))throw new InvalidDataException("Invalid resource name: "+name);}
    internal static void CheckPath(string path,bool root=false,bool glob=false)
    {
        if(root && path==".")return;
        if(glob) {if(path.Contains('[') || path.Contains(']'))throw new InvalidDataException("Only *, ? and ** glob syntax is supported");path=path.Replace('*','x').Replace('?','x');}
        ModulePackage.ValidatePath(path);
    }
    internal static Regex Glob(string pattern)
    {
        var text="^";
        for(var i=0;i<pattern.Length;i++)
        {
            if(pattern[i]=='*' && i+1<pattern.Length && pattern[i+1]=='*')
            {i++;if(i+1<pattern.Length && pattern[i+1]=='/') {i++;text+="(?:.*/)?";}else text+=".*";}
            else text+=pattern[i] switch {'*'=>"[^/]*",'?'=>"[^/]",_=>Regex.Escape(pattern[i].ToString())};
        }
        return new(text+"$",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    }
}

internal abstract class ResourceInput
{
    private readonly Dictionary<string,ReadOnlyMemory<byte>> read=new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,ReadOnlyMemory<byte>> Files=>read;
    protected abstract byte[] ReadFile(string path);
    protected abstract (bool File,IEnumerable<string> Paths) Enumerate(string path);
    public byte[] Read(string path)
    {
        ResourceSource.CheckPath(path);
        if(read.TryGetValue(path,out var existing))return existing.ToArray();
        var bytes=ReadFile(path);
        if(bytes.Length>ModuleContentLimits.MaxFileBytes || read.Count>=ModuleContentLimits.MaxFiles || read.Values.Sum(v=>(long)v.Length)+bytes.Length>ModuleContentLimits.MaxTotalBytes)
            throw new InvalidDataException("Resource content size budget exceeded");
        read.Add(path,bytes);return bytes;
    }
    public IEnumerable<(string Path,string Name)> Match(ResourceSource source,Func<string,bool>? defaultFilter=null)
    {
        var selection=Enumerate(source.Path);
        if(selection.File && source.Pattern!=null)throw new InvalidDataException("Resource pattern requires a directory: "+source.Path);
        if(!selection.File && source.Name!=null)throw new InvalidDataException("Resource name requires a single file: "+source.Path);
        var pattern=ResourceSource.Glob(source.Pattern ?? "**/*");var count=0;var scanned=0;
        foreach(var path in selection.Paths.Take(ModuleContentLimits.MaxFiles+1).Order(StringComparer.Ordinal))
        {
            if(++scanned>ModuleContentLimits.MaxFiles)throw new InvalidDataException("Resource scan file budget exceeded");
            var relative=selection.File?System.IO.Path.GetFileName(path):source.Path=="."?path:path[(source.Path.Length+1)..];
            if(!selection.File && !pattern.IsMatch(relative))continue;
            if(!selection.File && source.Pattern==null && defaultFilter!=null && !defaultFilter(path))continue;
            var name=source.Prefix+(source.Name ?? System.IO.Path.ChangeExtension(relative,null));ResourceSource.CheckName(name);
            count++;yield return(path,name);
        }
        if(count==0)throw new InvalidDataException("Resource source matched no files: "+source.Path+" / "+source.Pattern);
    }
}

internal sealed class DirectoryResourceInput(string directory) : ResourceInput
{
    private readonly string root=System.IO.Path.GetFullPath(directory);
    protected override byte[] ReadFile(string path)=>File.ReadAllBytes(DirectoryModule.SafeFile(root,path));
    protected override (bool File,IEnumerable<string> Paths) Enumerate(string path)
    {
        ResourceSource.CheckPath(path,true);
        var full=path=="."?root:System.IO.Path.Combine(root,path);
        if(File.Exists(full)) {_=DirectoryModule.SafeFile(root,path);return(true,new[]{path});}
        if(!Directory.Exists(full))throw new InvalidDataException(path+": missing resource path");
        for(var current=new DirectoryInfo(full);current!=null;current=current.Parent)
        {if((current.Attributes & FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(path+": linked resource directories are unsupported");if(current.FullName==root)break;}
        return(false,Walk(full));
    }
    private IEnumerable<string> Walk(string folder)
    {
        foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if((entry.Attributes & FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(entry.Name+": linked resource paths are unsupported");
            if(entry is DirectoryInfo)foreach(var path in Walk(entry.FullName))yield return path;
            else yield return System.IO.Path.GetRelativePath(root,entry.FullName).Replace('\\','/');
        }
    }
}

internal sealed class SnapshotResourceInput(ResourcePool pool) : ResourceInput
{
    protected override byte[] ReadFile(string path)=>pool.GetFile(path).ToArray();
    protected override (bool File,IEnumerable<string> Paths) Enumerate(string path)=>pool.Files.ContainsKey(path)
        ?(true,new[]{path}):(false,pool.Files.Keys.Where(p=>path=="." || p.StartsWith(path+"/",StringComparison.Ordinal)));
}
