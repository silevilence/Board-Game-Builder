using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

internal sealed class ContentSource(Func<string,byte[]> read,Func<string,IEnumerable<string>> enumerate) : IContentSource
{
    internal const int MaxFiles=2048,MaxFileBytes=32*1024*1024,MaxTotalBytes=128*1024*1024;
    internal SortedDictionary<string,byte[]> Files {get;}=new(StringComparer.Ordinal);
    private readonly HashSet<string> paths=new(StringComparer.OrdinalIgnoreCase);
    private long bytes;
    public IReadOnlyList<string> Enumerate(string path)
    {
        if(path!=".") ContentPath.Validate(path);
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<string>();
        foreach(var file in enumerate(path))
        {
            ContentPath.Validate(file);
            if(file.Equals(GameRuntime.Entry,StringComparison.OrdinalIgnoreCase) || file.Equals(GamePackage.Manifest,StringComparison.OrdinalIgnoreCase)) continue;
            if(file!=path && path!="." && !file.StartsWith(path+"/",StringComparison.Ordinal)) throw new InvalidDataException("Enumeration escaped requested content path");
            if(!names.Add(file) || result.Count>=MaxFiles) throw new InvalidDataException("Content enumeration count/duplicate path budget exceeded");
            result.Add(file);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    public ReadOnlyMemory<byte> Read(string path)
    {
        if(path.Equals(GameRuntime.Entry,StringComparison.OrdinalIgnoreCase) || path.Equals(GamePackage.Manifest,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(path+": reserved content path");
        return ReadFile(path).ToArray();
    }
    internal byte[] ReadFile(string path)
    {
        ContentPath.Validate(path);
        if(Files.TryGetValue(path,out var existing)) return existing;
        if(Files.Count>=MaxFiles || !paths.Add(path)) throw new InvalidDataException(path+": content count/duplicate case-insensitive path");
        var data=read(path);
        if(data.Length>MaxFileBytes || bytes+data.Length>MaxTotalBytes) throw new InvalidDataException(path+": content size budget exceeded");
        bytes+=data.Length;Files.Add(path,data.ToArray());return Files[path];
    }
    internal static ContentSource Directory(string directory)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return new(path=>
        {
            ContentPath.Validate(path);
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked content roots unsupported");
            var full=Path.GetFullPath(Path.Combine(root,path));
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                throw new InvalidDataException(path+": missing file or path escapes content root");
            for(FileSystemInfo? current=new FileInfo(full);current!=null && current.FullName!=root;current=System.IO.Directory.GetParent(current.FullName))
                if((current.Attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException(path+": linked content unsupported");
            using var stream=File.OpenRead(full);return ReadBounded(stream,MaxFileBytes,path);
        },EnumerateDirectory);
        IEnumerable<string> EnumerateDirectory(string path)
        {
            var full=path=="."?root:Path.GetFullPath(Path.Combine(root,path));
            if(full!=root && !full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Content path escapes root");
            if(!File.Exists(full) && !System.IO.Directory.Exists(full)) throw new InvalidDataException(path+": missing content path");
            for(FileSystemInfo? current=File.Exists(full)?new FileInfo(full):new DirectoryInfo(full);current!=null;current=System.IO.Directory.GetParent(current.FullName))
            {
                if((current.Attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked content paths unsupported");
                if(current.FullName==root) break;
            }
            if(File.Exists(full)) {yield return path;yield break;}
            if(!System.IO.Directory.Exists(full)) throw new InvalidDataException(path+": missing content path");
            foreach(var file in Walk(full)) yield return file;
        }
        IEnumerable<string> Walk(string folder)
        {
            foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if((entry.Attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked content paths unsupported");
                if(entry is DirectoryInfo) {foreach(var file in Walk(entry.FullName)) yield return file;}
                else yield return Path.GetRelativePath(root,entry.FullName).Replace('\\','/');
            }
        }
    }
    internal static ContentSource Snapshot(IReadOnlyDictionary<string,byte[]> files)=>new(
        path=>files.TryGetValue(path,out var bytes)?bytes:throw new InvalidDataException(path+": missing snapshot/package content"),
        path=>files.Keys.Where(p=>p==path || path=="." || p.StartsWith(path+"/",StringComparison.Ordinal)));
    internal static byte[] ReadBounded(Stream stream,int limit,string path)
    {
        if(stream.CanSeek && stream.Length>limit) throw new InvalidDataException(path+": size budget exceeded");
        using var buffer=new MemoryStream();byte[] block=new byte[81920];int count;
        while((count=stream.Read(block))>0)
        {
            if(buffer.Length+count>limit) throw new InvalidDataException(path+": expanded size budget exceeded");
            buffer.Write(block,0,count);
        }
        return buffer.ToArray();
    }
}
