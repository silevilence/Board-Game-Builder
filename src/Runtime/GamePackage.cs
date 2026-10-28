using System.IO.Compression;
using System.Text;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

public sealed record GamePackageManifest(int Format,string Fingerprint,SortedDictionary<string,string> Files);
public static class GamePackage
{
    public static void Pack(string directory,string destination,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        var module=GameRuntime.LoadDirectory(directory,approvedPlugins:approvedPlugins);
        foreach(var preset in GamePresets.Choices(module.RuntimeIdentity!.Startup,approvedPlugins).Keys) _=GameRuntime.LoadDirectory(directory,preset,approvedPlugins);
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var path in module.RuntimeIdentity!.ContentHashes.Keys.Append(GameRuntime.Entry))
            files.Add(path,File.ReadAllBytes(DirectoryModule.SafeFile(directory,path)));
        if(files.Count>ModuleContentLimits.MaxFiles || files.Any(f=>f.Value.Length>ModuleContentLimits.MaxFileBytes) || files.Sum(f=>(long)f.Value.Length)>ModuleContentLimits.MaxTotalBytes) throw new InvalidDataException("Package size budget exceeded");
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
            _=Load(temp,approvedPlugins:approvedPlugins);File.Move(temp,target,false);
        }
        finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    public static LoadedModule Load(string path,string? preset=null,IReadOnlyList<RuntimePluginDefinition>? approvedPlugins=null)
    {
        var temp=Path.Combine(Path.GetTempPath(),"bgb-runtime-package-"+Guid.NewGuid());
        try
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>ModuleContentLimits.MaxFiles+1 || zip.Entries.Any(e=>e.Length>ModuleContentLimits.MaxFileBytes) || zip.Entries.Sum(e=>e.Length)>ModuleContentLimits.MaxTotalBytes) throw new InvalidDataException("Package size budget exceeded");
            var entries=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries)
            {
                ModulePackage.ValidatePath(entry.FullName);
                using var stream=entry.Open();using var data=new MemoryStream();var buffer=new byte[81920];int read;
                while((read=stream.Read(buffer))>0)
                {
                    if(data.Length+read>ModuleContentLimits.MaxFileBytes || data.Length+read>entry.Length) throw new InvalidDataException(entry.FullName+": expanded size exceeded");
                    data.Write(buffer,0,read);
                }
                if(!entries.TryAdd(entry.FullName,data.ToArray())) throw new InvalidDataException(entry.FullName+": duplicate package path");
            }
            if(!entries.Remove("package.manifest.json",out var bytes)) throw new InvalidDataException("Missing package manifest");
            using var header=System.Text.Json.JsonDocument.Parse(bytes);
            if(!header.RootElement.TryGetProperty("format",out var format) || !format.TryGetInt32(out var version) || version!=2)
                throw new InvalidDataException("Legacy/incompatible package format: explicitly migrate the source directory and repack");
            var manifest=Wire.Read<GamePackageManifest>(Encoding.UTF8.GetString(bytes));
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
            var module=GameRuntime.LoadDirectory(temp,approvedPlugins:approvedPlugins);
            if(module.Fingerprint!=manifest.Fingerprint) throw new InvalidDataException("Package runtime fingerprint mismatch");
            var expected=module.RuntimeIdentity!.ContentHashes.Keys.Append(GameRuntime.Entry).ToHashSet(StringComparer.Ordinal);
            if(!expected.SetEquals(manifest.Files.Keys)) throw new InvalidDataException("Package contains undeclared content");
            return preset==null?module:GameRuntime.LoadDirectory(temp,preset,approvedPlugins);
        }
        catch(Exception error) when(error is not OutOfMemoryException) {throw new InvalidDataException(path+": "+error.Message,error);}
        finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
    }
}
