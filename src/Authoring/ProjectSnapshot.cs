namespace BoardGame.Authoring;

public sealed class ProjectSnapshot : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "bgb-project-" + Guid.NewGuid());
    public ProjectSource Source { get; }
    public ProjectSnapshot(ProjectView view, IReadOnlyDictionary<string, byte[]?>? changes = null)
    {
        Source = view.Source;
        var files = view.Paths.ToDictionary(p => p, view.Bytes, StringComparer.OrdinalIgnoreCase);
        if (changes != null) foreach (var (path, bytes) in changes)
        { if (bytes == null) files.Remove(path); else files[path] = bytes; }
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var (path, bytes) in files)
            {
                var target = ProjectPaths.Resolve(Directory, path);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}
