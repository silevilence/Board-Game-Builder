using BoardGame.AppRuntime;

namespace BoardGame.Runtime;

/// <summary>Resource discovery captures bytes through the same content source used by
/// directory, package and snapshot loading, including wildcard-selected files.</summary>
public sealed class ContentResourceInput(IContentSource source) : ResourceInput
{
    protected override byte[] ReadFile(string path)=>source.Read(path).ToArray();
    protected override (bool File,IEnumerable<string> Paths) Enumerate(string path)
    {
        var files=source.Enumerate(path);
        return(files.Contains(path,StringComparer.Ordinal),files);
    }
}
