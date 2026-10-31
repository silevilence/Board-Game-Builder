using System.Text;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

/// <summary>Format-neutral entry. BoardGame.Runtime.GameRuntime retains the legacy typed board payload.</summary>
public static class GameRuntime
{
    public const string Entry="game.start.json";
    public static GameContent Load(string source,ContentCatalog catalog,string? preset=null)
    {
        if(Directory.Exists(source)) return LoadDirectory(source,catalog,preset);
        if(Path.GetFileName(source).Equals(Entry,StringComparison.OrdinalIgnoreCase)) return LoadDirectory(Path.GetDirectoryName(Path.GetFullPath(source))!,catalog,preset);
        if(Path.GetExtension(source).Equals(".bgm",StringComparison.OrdinalIgnoreCase)) return GamePackage.Load(source,catalog,preset);
        if(preset!=null) throw new InvalidDataException("Snapshots already select a preset");
        using var stream=File.OpenRead(source);
        var snapshot=ContentJson.Read<ContentSnapshot>(ContentSource.ReadBounded(stream,ContentSource.MaxTotalBytes*2,source));
        return Restore(snapshot,catalog);
    }
    public static GameContent LoadDirectory(string directory,ContentCatalog catalog,string? preset=null)=>Build(ContentSource.Directory(directory),catalog,preset);
    public static GameContent LoadFiles(IReadOnlyDictionary<string,byte[]> files,ContentCatalog catalog,string? preset=null)
    {
        var copy=files.ToDictionary(p=>p.Key,p=>p.Value.ToArray(),StringComparer.Ordinal);
        ValidateFiles(copy);return Build(ContentSource.Snapshot(copy),catalog,preset);
    }
    public static GameContent Restore(ContentSnapshot original,ContentCatalog catalog)
    {
        var snapshot=ContentJson.Copy(original);
        if(snapshot.Kind!="application-content" || snapshot.Format!=1 || snapshot.Identity?.Format!=1 || snapshot.Files==null || snapshot.Payload==null)
            throw new InvalidDataException("Unknown application snapshot/identity version");
        ValidateFiles(snapshot.Files);
        var restored=Build(ContentSource.Snapshot(snapshot.Files),catalog,snapshot.Preset);
        if(!restored.Snapshot.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(snapshot.Files.Keys)) throw new InvalidDataException("Snapshot contains undeclared content");
        if(snapshot.Payload.Format!=restored.Payload.Format || snapshot.Payload.Version!=restored.Payload.Version)
            throw new InvalidDataException("Unknown payload format/version");
        if(ContentJson.Canonical(snapshot.Identity)!=ContentJson.Canonical(restored.Identity) ||
            ContentJson.Canonical(snapshot.Payload)!=ContentJson.Canonical(restored.Payload) || snapshot.Fingerprint!=restored.Fingerprint)
            throw new InvalidDataException("Snapshot content/payload identity mismatch");
        return restored;
    }
    internal static GameContent Build(ContentSource source,ContentCatalog catalog,string? preset)
    {
        var entry=source.ReadFile(Entry);
        if(entry.Length>1024*1024) throw new InvalidDataException(Entry+": size limit exceeded");
        var startup=RuntimeDiscovery.Validate(ContentJson.Read<RuntimeStartup>(entry),catalog.Plugins);
        var (selection,definition)=RuntimeDiscovery.Definition(startup,catalog.Plugins);
        var contract=catalog.Plugins.Single(p=>p.Id==selection.Id).Content!;
        RuntimeStartup Select(string path)=>startup with {Plugins=startup.Plugins.Select(p=>p.Id!=selection.Id?p:p with
        {Configuration=(contract.SelectSource ?? throw new InvalidDataException(selection.Id+": preset selection unavailable"))(p.Configuration,path)}).ToList()};
        if(preset!=null && !definition.Presets.ContainsKey(preset)) throw new InvalidDataException("Unknown definition preset: "+preset);
        var effective=preset==null?startup:Select(definition.Presets[preset]);
        // Validate and capture every preset's dependencies, without activating runtime behavior.
        foreach(var path in definition.Presets.Values.Distinct(StringComparer.Ordinal))
            if(path!=RuntimeDiscovery.Definition(effective,catalog.Plugins).Sources.Source) _=Prepare(Select(path));
        var payload=Prepare(effective);
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in source.Files.Where(p=>p.Key!=Entry)) hashes.Add(file.Key,ContentJson.Hash(file.Value));
        var identity=new ContentIdentity(1,effective,RuntimeDiscovery.Definition(effective,catalog.Plugins).Sources.Source,hashes);
        var fingerprint=ContentJson.Hash(Encoding.UTF8.GetBytes(ContentJson.Canonical(new ContentFingerprintInput(identity,payload))));
        return new(new("application-content",1,preset,identity,payload,source.Files,fingerprint));

        ContentPayload Prepare(RuntimeStartup selected)
        {
            RuntimeDiscovery.Validate(selected,catalog.Plugins);
            foreach(var file in RuntimeDiscovery.Files(selected,catalog.Plugins)) _=source.Read(file);
            _=source.Read(RuntimeDiscovery.Definition(selected,catalog.Plugins).Sources.Source);
            var preparation=RuntimeDiscovery.Preparation(selected,catalog.Plugins);
            using var host=RuntimeHost.StartAsync(preparation,catalog.Plugins,["definition"]).GetAwaiter().GetResult();
            var loader=host.Service<IContentLoader>("definition");
            if(string.IsNullOrWhiteSpace(loader.PayloadFormat) || loader.PayloadVersion<1) throw new InvalidDataException("Invalid payload contract");
            try
            {
                var data=loader.Load(source,ContentJson.Copy(selected));
                if(data.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null || Encoding.UTF8.GetByteCount(data.GetRawText())>ContentSource.MaxFileBytes)
                    throw new InvalidDataException("Invalid/oversized payload");
                return new(loader.PayloadFormat,loader.PayloadVersion,data.Clone());
            }
            catch(Exception error) when(error is not OutOfMemoryException)
            {throw new InvalidDataException(selection.Id+": content preparation failed: "+error.Message,error);}
        }
    }
    internal static void ValidateFiles(IReadOnlyDictionary<string,byte[]> files)
    {
        if(files.Count>ContentSource.MaxFiles || files.Any(p=>p.Value==null || p.Value.Length>ContentSource.MaxFileBytes) || files.Sum(p=>(long)p.Value.Length)>ContentSource.MaxTotalBytes)
            throw new InvalidDataException("Content size/count budget exceeded");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            ContentPath.Validate(file.Key);
            if(!paths.Add(file.Key) || file.Key.Equals(GamePackage.Manifest,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Duplicate/reserved content path: "+file.Key);
        }
    }
}
