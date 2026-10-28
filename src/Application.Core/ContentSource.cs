using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

internal sealed class ContentSource(Func<string,byte[]> read) : IContentSource
{
    internal const int MaxFiles=2048,MaxFileBytes=32*1024*1024,MaxTotalBytes=128*1024*1024;
    internal SortedDictionary<string,byte[]> Files {get;}=new(StringComparer.Ordinal);
    private readonly HashSet<string> paths=new(StringComparer.OrdinalIgnoreCase);
    private long bytes;
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
        });
    }
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
