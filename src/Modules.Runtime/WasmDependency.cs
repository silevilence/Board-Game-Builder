namespace BoardGame.Modules;

/// <summary>Runtime WASM declaration shared by current loading and explicit legacy migration.</summary>
public static class WasmDependency
{
    public static void Validate(RuntimeDependency dependency)
    {
        var d=dependency;
        if(d.Kind!="wasm" || d.EditorOnly || string.IsNullOrWhiteSpace(d.Id) || d.Id is "kernel" or "lua" or "view.monogame" ||
            d.Api!=1 || !int.TryParse(d.Version,out var version) || version<1 || version.ToString()!=d.Version ||
            d.File==null || !d.File.EndsWith(".wasm",StringComparison.Ordinal) || d.Sha256==null || d.Sha256.Length!=64 || !d.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException($"plugins[{d.Id}]: incompatible WASM API/version/file/hash");
        if(d.Id.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_'))throw new InvalidDataException($"plugins[{d.Id}]: invalid WASM identity");
        if(d.Permissions!=null && (d.Permissions.Distinct().Count()!=d.Permissions.Count || d.Permissions.Any(p=>p is not ("component" or "command" or "snap" or "container"))))
            throw new InvalidDataException($"plugins[{d.Id}]: unsupported permission");
        var path=d.File;
        if(path.Length>240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(p=>p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
            throw new InvalidDataException($"{path}: unsafe package path");
    }
    public static void ValidateAll(IEnumerable<RuntimeDependency> dependencies)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var dependency in dependencies)
        {
            if(dependency==null || !ids.Add(dependency.Id))throw new InvalidDataException("plugins: duplicate/conflicting dependency");
            Validate(dependency);
        }
    }
}
