using System.IO.Compression;
using System.Text;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

public sealed record GamePackageManifest(int Format,string Fingerprint,SortedDictionary<string,string> Files);
public static class GamePackage
{
    private const int MaxFile=32*1024*1024,MaxTotal=128*1024*1024;
    public static void Pack(string directory,string destination)
    {
        var module=GameRuntime.LoadDirectory(directory);
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var path in module.RuntimeIdentity!.ContentHashes.Keys.Append(GameRuntime.Entry))
            files.Add(path,File.ReadAllBytes(DirectoryModule.SafeFile(directory,path)));
        if(files.Count>2048 || files.Any(f=>f.Value.Length>MaxFile) || files.Sum(f=>(long)f.Value.Length)>MaxTotal) throw new InvalidDataException("Package size budget exceeded");
        var hashes=new SortedDictionary<string,string>(files.ToDictionary(f=>f.Key,f=>ModulePackage.Hash(f.Value)),StringComparer.Ordinal);
        var manifest=new GamePackageManifest(2,module.Fingerprint,hashes);
        var target=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp=target+"."+Guid.NewGuid()+".tmp";
        try
        {
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                foreach(var file in files) {using var stream=zip.CreateEntry(file.Key).Open();stream.Write(file.Value);}
                using var writer=new StreamWriter(zip.CreateEntry("package.manifest.json").Open(),new UTF8Encoding(false));writer.Write(Wire.Json(manifest));
            }
            _=Load(temp);File.Move(temp,target,false);
        }
        finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    public static LoadedModule Load(string path)
    {
        var temp=Path.Combine(Path.GetTempPath(),"bgb-runtime-package-"+Guid.NewGuid());
        try
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>2049 || zip.Entries.Any(e=>e.Length>MaxFile) || zip.Entries.Sum(e=>e.Length)>MaxTotal) throw new InvalidDataException("Package size budget exceeded");
            var entries=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries)
            {
                ModulePackage.ValidatePath(entry.FullName);
                using var stream=entry.Open();using var data=new MemoryStream();var buffer=new byte[81920];int read;
                while((read=stream.Read(buffer))>0)
                {
                    if(data.Length+read>MaxFile || data.Length+read>entry.Length) throw new InvalidDataException(entry.FullName+": expanded size exceeded");
                    data.Write(buffer,0,read);
                }
                if(!entries.TryAdd(entry.FullName,data.ToArray())) throw new InvalidDataException(entry.FullName+": duplicate package path");
            }
            if(!entries.Remove("package.manifest.json",out var bytes)) throw new InvalidDataException("Missing package manifest");
            var manifest=Wire.Read<GamePackageManifest>(Encoding.UTF8.GetString(bytes));
            if(manifest.Format!=2) throw new InvalidDataException("Legacy/incompatible package format: explicitly migrate the source directory and repack");
            if(manifest.Files.Count!=entries.Count) throw new InvalidDataException("Package file count mismatch");
            foreach(var file in manifest.Files)
            {
                ModulePackage.ValidatePath(file.Key);
                if(!entries.TryGetValue(file.Key,out var content) || ModulePackage.Hash(content)!=file.Value) throw new InvalidDataException(file.Key+": content hash mismatch");
            }
            Directory.CreateDirectory(temp);
            foreach(var file in entries)
            {
                var destination=Path.Combine(temp,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllBytes(destination,file.Value);
            }
            var module=GameRuntime.LoadDirectory(temp);
            if(module.Fingerprint!=manifest.Fingerprint) throw new InvalidDataException("Package runtime fingerprint mismatch");
            var expected=module.RuntimeIdentity!.ContentHashes.Keys.Append(GameRuntime.Entry).ToHashSet(StringComparer.Ordinal);
            if(!expected.SetEquals(manifest.Files.Keys)) throw new InvalidDataException("Package contains undeclared content");
            return module;
        }
        catch(Exception error) when(error is not OutOfMemoryException) {throw new InvalidDataException(path+": "+error.Message,error);}
        finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
    }
}
