using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.Kernel;
namespace BoardGame.Modules;

public sealed record PackageManifest(int Format,int Api,string Source,string ModuleId,string ModuleVersion,string Fingerprint,
    List<RuntimeDependency> Dependencies,SortedDictionary<string,string> Files);
public static class ModulePackage
{
    public static readonly IReadOnlyDictionary<string,string> Official=new Dictionary<string,string>
    { ["kernel"]="1",["lua"]="2.0.0",["view.monogame"]="3.8.4.1" };
    public static List<RuntimeDependency> Dependencies(JsonObject root)
    {
        if(root["api"] is {} api && api.GetValue<int>()!=EngineVersion.Api) throw new InvalidDataException("module.json: incompatible API version");
        var deps=root["plugins"]?.Deserialize<List<RuntimeDependency>>(Wire.Options) ?? [];
        ValidateDependencies(deps);
        var runtime=deps.Where(d=>!d.EditorOnly).ToList();
        foreach(var official in Official) if(!runtime.Any(d=>d.Id==official.Key)) runtime.Add(new(official.Key,official.Value,EngineVersion.Api));
        return runtime.OrderBy(d=>d.Id,StringComparer.Ordinal).ToList();
    }
    public static void ValidateDependencies(IEnumerable<RuntimeDependency> dependencies)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var d in dependencies)
        {
            if(d==null || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id)) throw new InvalidDataException("plugins: duplicate/conflicting dependency");
            if(d.EditorOnly) continue;
            if(d.Kind=="wasm")
            {
                if(Official.ContainsKey(d.Id) || d.Api!=1 || !int.TryParse(d.Version,out var pluginVersion) || pluginVersion<1 || pluginVersion.ToString()!=d.Version ||
                   d.File==null || !d.File.EndsWith(".wasm",StringComparison.Ordinal) || d.Sha256==null || d.Sha256.Length!=64 || !d.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"plugins[{d.Id}]: incompatible WASM API/version/file/hash");
                ValidatePath(d.File);
                if(d.Id.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_')) throw new InvalidDataException($"plugins[{d.Id}]: invalid WASM identity");
                if(d.Permissions!=null && (d.Permissions.Distinct().Count()!=d.Permissions.Count || d.Permissions.Any(p=>p is not ("component" or "command" or "snap" or "container")))) throw new InvalidDataException($"plugins[{d.Id}]: unsupported permission");
                continue;
            }
            if(d.Kind is not (null or "native")) throw new InvalidDataException($"plugins[{d.Id}]: unsupported plugin kind");
            if(!Official.TryGetValue(d.Id,out var version)) throw new InvalidDataException($"plugins[{d.Id}]: missing/unsupported runtime dependency");
            if(d.Api!=EngineVersion.Api || d.Version!=version) throw new InvalidDataException($"plugins[{d.Id}]: incompatible API/version");
        }
    }
    public static LoadedModule Load(string source) => Directory.Exists(source)?DirectoryModule.Load(source):
        Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase)?LoadPackage(source):ModuleSnapshot.Load(source);

    public static void Pack(string directory,string destination)
    {
        var module=DirectoryModule.Load(directory);
        var root=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"module.json")))!.AsObject();
        var deps=Dependencies(root); root["plugins"]=System.Text.Json.JsonSerializer.SerializeToNode(deps,Wire.Options);
        var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal) { ["module.json"]=Encoding.UTF8.GetBytes(root.ToJsonString()) };
        foreach(var relative in ReferencedFiles(root)) files[relative]=File.ReadAllBytes(DirectoryModule.SafeFile(directory,relative));
        if(files.Count>ModuleContentLimits.MaxFiles || files.Any(f=>f.Value.Length>ModuleContentLimits.MaxFileBytes) || files.Sum(f=>(long)f.Value.Length)>ModuleContentLimits.MaxTotalBytes) throw new InvalidDataException("Package size budget exceeded");
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var f in files) hashes[f.Key]=Hash(f.Value);
        var manifest=new PackageManifest(1,EngineVersion.Api,module.Id+"@"+module.Version,module.Id,module.Version,module.Fingerprint,deps,hashes);
        var full=Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp=full+"."+Guid.NewGuid()+".tmp";
        try
        {
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                foreach(var file in files) { using var stream=zip.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
                using var writer=new StreamWriter(zip.CreateEntry("package.manifest.json").Open(),new UTF8Encoding(false)); writer.Write(Wire.Json(manifest));
            }
            _=LoadPackage(temp); File.Move(temp,full,false);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public static IEnumerable<string> ReferencedFiles(JsonObject root)
    {
        var paths=new List<string>();
        foreach(var key in new[]{"rules","cpu"}) if(root[key]!=null) paths.Add(root[key]!.GetValue<string>());
        if(root["resources"] is JsonObject resources)paths.AddRange(resources.Select(p=>p.Value!.GetValue<string>()));
        if(root["runtimeFiles"] is JsonArray runtime) paths.AddRange(runtime.Select(p=>p!.GetValue<string>()));
        paths.AddRange(Dependencies(root).Where(d=>d.Kind=="wasm").Select(d=>d.File!));
        foreach(var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ValidatePath(path);
            if(path.Equals("module.json",StringComparison.OrdinalIgnoreCase) || path.Equals("package.manifest.json",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{path}: reserved module path");
            yield return path;
        }
    }
    public static void ValidatePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || path.Length>240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
           path.Split('/').Any(p=>p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
            throw new InvalidDataException($"{path}: unsafe package path");
    }
    public static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
    private static LoadedModule LoadPackage(string path)
    {
        var temp=Path.Combine(Path.GetTempPath(),"bgb-package-"+Guid.NewGuid());
        try
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>ModuleContentLimits.MaxFiles+1 || zip.Entries.Any(e=>e.Length>ModuleContentLimits.MaxFileBytes) || zip.Entries.Sum(e=>e.Length)>ModuleContentLimits.MaxTotalBytes) throw new InvalidDataException("Package size budget exceeded");
            var entries=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries)
            {
                ValidatePath(entry.FullName);
                using var input=entry.Open(); using var buffer=new MemoryStream(); var chunk=new byte[81920]; int read;
                while((read=input.Read(chunk))>0)
                {
                    if(buffer.Length+read>ModuleContentLimits.MaxFileBytes || buffer.Length+read>entry.Length) throw new InvalidDataException($"{entry.FullName}: expanded content exceeds declared size/budget");
                    buffer.Write(chunk,0,read);
                }
                if(!entries.TryAdd(entry.FullName,buffer.ToArray())) throw new InvalidDataException($"{entry.FullName}: duplicate package entry");
            }
            if(!entries.Remove("package.manifest.json",out var manifestBytes)) throw new InvalidDataException("Missing package manifest");
            var manifest=Wire.Read<PackageManifest>(Encoding.UTF8.GetString(manifestBytes));
            if(manifest.Format!=1 || manifest.Api!=EngineVersion.Api) throw new InvalidDataException("Incompatible package format/API version");
            ValidateDependencies(manifest.Dependencies);
            if(manifest.Dependencies.Any(d=>d.EditorOnly)) throw new InvalidDataException("Package contains editor_only dependencies");
            if(manifest.Files.Count!=entries.Count) throw new InvalidDataException("Package manifest file count mismatch");
            foreach(var f in manifest.Files)
            {
                ValidatePath(f.Key);
                if(!entries.TryGetValue(f.Key,out var bytes) || Hash(bytes)!=f.Value) throw new InvalidDataException($"{f.Key}: missing or content hash mismatch");
            }
            Directory.CreateDirectory(temp);
            foreach(var f in entries)
            {
                var target=Path.Combine(temp,f.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target,f.Value);
            }
            var module=DirectoryModule.Load(temp);
            var root=JsonNode.Parse(Encoding.UTF8.GetString(entries["module.json"]))!.AsObject();
            if(Wire.Json(Dependencies(root))!=Wire.Json(manifest.Dependencies)) throw new InvalidDataException("Conflicting package dependencies");
            if(module.Id!=manifest.ModuleId || module.Version!=manifest.ModuleVersion || module.Fingerprint!=manifest.Fingerprint)
                throw new InvalidDataException("Package module identity/fingerprint mismatch");
            return module with { RulesPath=path+"!/"+root["rules"]!.GetValue<string>(),CpuPath=path+"!/"+root["cpu"]!.GetValue<string>() };
        }
        catch(Exception e) when(e is not OutOfMemoryException) { throw new InvalidDataException($"{path}: {e.Message}",e); }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }
}
