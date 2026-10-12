using System.Text.RegularExpressions;

namespace BoardGame.Launcher;

internal static class ExportCacheOwnership
{
    internal static void Add(string directory,ProductManifest manifest,HashSet<string> owned)
    {
        if(manifest.Mode!="slim" || !manifest.Json.TryGetProperty("exportDependencies",out var export)) return;
        const string cache=".state/export-dependencies/";owned.Add(cache+"cache.lock");
        var files=export.GetProperty("files").EnumerateArray().ToArray();
        var hashes=files.Select(f=>f.GetProperty("sha256").GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach(var hash in hashes) owned.Add(cache+"objects/"+hash);
        var descriptors=manifest.Files.Where(f=>f.Path.EndsWith("/export.dependencies.json",StringComparison.Ordinal)).Select(f=>f.Sha256).ToHashSet(StringComparer.Ordinal);
        var players=ProductManifest.SafePath(directory,cache+"players");var identities=descriptors.ToList();
        if(Directory.Exists(players))
            foreach(var path in Directory.EnumerateDirectories(players))
            {
                var name=Path.GetFileName(path);var parts=name.Split(".staging-",StringSplitOptions.None);
                if(parts.Length==2&&descriptors.Contains(parts[0])&&Guid.TryParseExact(parts[1],"N",out _)) identities.Add(name);
            }
        foreach(var identity in identities) foreach(var file in files) owned.Add(cache+"players/"+identity+"/"+file.GetProperty("path").GetString());
        var objects=ProductManifest.SafePath(directory,cache+"objects");
        if(Directory.Exists(objects))
            foreach(var path in Directory.EnumerateFiles(objects))
            {
                var name=Path.GetFileName(path);var parts=name.Split(".partial-",StringSplitOptions.None);
                if(parts.Length==2&&hashes.Contains(parts[0])&&Guid.TryParseExact(parts[1],"N",out _)) owned.Add(cache+"objects/"+name);
            }
        var root=ProductManifest.SafePath(directory,cache.TrimEnd('/'));
        if(Directory.Exists(root)) foreach(var path in Directory.EnumerateFiles(root))
            if(Regex.IsMatch(Path.GetFileName(path),@"\Adownload-[0-9a-f]{32}\.zip\z")) owned.Add(cache+Path.GetFileName(path));
    }
}
