using System.IO.Compression;
using System.Text;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

/// <summary>Application package format 3. Format 2 stays with the explicit board-game adapter.</summary>
public static class GamePackage
{
    internal const string Manifest="package.manifest.json";
    private sealed record PackageManifest(int Format,string Fingerprint,SortedDictionary<string,string> Files);
    public static void Pack(string directory,string destination,ContentCatalog catalog)
    {
        var content=GameRuntime.LoadDirectory(directory,catalog);
        var snapshot=content.Snapshot;
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in snapshot.Files) hashes.Add(file.Key,ContentJson.Hash(file.Value));
        var manifest=new PackageManifest(3,content.Fingerprint,hashes);
        var target=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary=target+"."+Guid.NewGuid()+".tmp";
        try
        {
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create))
            {
                foreach(var file in snapshot.Files) {using var stream=zip.CreateEntry(file.Key).Open();stream.Write(file.Value);}
                using var writer=new StreamWriter(zip.CreateEntry(Manifest).Open(),new UTF8Encoding(false));writer.Write(ContentJson.Write(manifest));
            }
            _=Load(temporary,catalog);File.Move(temporary,target,false);
        }
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
    public static GameContent Load(string path,ContentCatalog catalog,string? preset=null)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>ContentSource.MaxFiles+1 || zip.Entries.Any(e=>e.Length>ContentSource.MaxFileBytes) || zip.Entries.Sum(e=>e.Length)>ContentSource.MaxTotalBytes)
            throw new InvalidDataException("Package size/count budget exceeded");
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            ContentPath.Validate(entry.FullName);
            if(!names.Add(entry.FullName)) throw new InvalidDataException(entry.FullName+": duplicate package path");
            using var stream=entry.Open();var bytes=ContentSource.ReadBounded(stream,ContentSource.MaxFileBytes,entry.FullName);
            if(bytes.Length!=entry.Length) throw new InvalidDataException(entry.FullName+": expanded size mismatch");
            files.Add(entry.FullName,bytes);
        }
        if(!files.Remove(Manifest,out var data)) throw new InvalidDataException("Missing package manifest");
        var manifest=ContentJson.Read<PackageManifest>(data);
        if(manifest.Format!=3 || manifest.Files==null) throw new InvalidDataException("Unsupported application package version; use the explicit legacy adapter");
        GameRuntime.ValidateFiles(files);
        if(!manifest.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(files.Keys)) throw new InvalidDataException("Package file set mismatch");
        foreach(var file in files)
            if(manifest.Files[file.Key]!=ContentJson.Hash(file.Value)) throw new InvalidDataException(file.Key+": package hash mismatch");
        ContentSource Source()=>ContentSource.Snapshot(files);
        var content=GameRuntime.Build(Source(),catalog,null);
        if(content.Fingerprint!=manifest.Fingerprint) throw new InvalidDataException("Package content identity mismatch");
        if(!content.Snapshot.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(files.Keys)) throw new InvalidDataException("Package contains undeclared content");
        return preset==null?content:GameRuntime.Build(Source(),catalog,preset);
    }
}
