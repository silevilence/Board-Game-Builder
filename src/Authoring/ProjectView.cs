using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoardGame.Authoring;

public interface IAuthorFiles
{
    string Text(string path);
    byte[] Bytes(string path);
    IReadOnlyList<string> Paths { get; }
}

public sealed record ProjectIdentity(string Directory, string Profile, string Mode, string? Workspace = null, long Generation = 0);
public sealed record ProjectSource(ProjectIdentity Identity, string Version);
public sealed record FileBaseline(string Path, string? Hash);
public sealed record EnumerationBaseline(string Prefix, string[] Paths);
public sealed record FileChange(string Path, byte[]? Content, string? Baseline);
public sealed record ProjectChanges(string RequestId, ProjectSource Source, FileChange[] Files,
    FileBaseline[] Reads, EnumerationBaseline[] Enumerations);
public sealed record AuthorDiagnostic(string Code, string Message, string? Path = null);
public enum EditState { Applied, Conflict, PartialFailure, Unknown, Rejected }
public sealed record EditReceipt(string RequestId, EditState State, bool Saved, ProjectSource Source,
    string? ResultVersion, AuthorDiagnostic[] Diagnostics, string[] AppliedPaths);

public interface IProjectEditMode : IAsyncDisposable
{
    Task<ProjectView> Read(CancellationToken cancellation = default);
    Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default);
    Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default);
}

// A view owns immutable bytes. Dependencies record what the operation actually observed.
public sealed class ProjectView : IAuthorFiles
{
    private readonly Dictionary<string, byte[]> files;
    private readonly Dictionary<string, FileBaseline> reads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnumerationBaseline> enumerations = new(StringComparer.OrdinalIgnoreCase);
    public ProjectSource Source { get; }

    public ProjectView(ProjectIdentity identity, IReadOnlyDictionary<string, byte[]> content)
    {
        files = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in content)
        {
            ProjectPaths.Validate(path);
            if (!files.TryAdd(path, bytes.ToArray())) throw new InvalidDataException("Duplicate project path: " + path);
        }
        Source = new(identity, Version(files));
    }
    public IReadOnlyList<string> Paths => Enumerate();
    public string[] Enumerate(string prefix = "")
    {
        if (prefix.Length > 0) ProjectPaths.Validate(prefix.TrimEnd('/'));
        var result = files.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        enumerations[prefix] = new(prefix, result.ToArray());
        return result;
    }
    public bool Exists(string path) { Observe(path); return files.ContainsKey(path); }
    public byte[] Bytes(string path) { Observe(path); return files[path].ToArray(); }
    public string Text(string path) => Encoding.UTF8.GetString(Bytes(path));
    private void Observe(string path)
    {
        ProjectPaths.Validate(path);
        reads[path] = new(path, files.TryGetValue(path, out var bytes) ? Hash(bytes) : null);
    }
    public ProjectChanges Change(string requestId, IReadOnlyDictionary<string, byte[]?> changes)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128) throw new ArgumentException("Request ID is required (maximum 128 characters)");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edits = changes.Select(p =>
        {
            Observe(p.Key);
            if (!targets.Add(p.Key)) throw new InvalidDataException("Duplicate change path: " + p.Key);
            return new FileChange(p.Key, p.Value?.ToArray(), reads[p.Key].Hash);
        }).ToArray();
        return new(requestId, Source, edits, reads.Values.ToArray(), enumerations.Values.ToArray());
    }
    public AuthorDiagnostic[] Conflicts(ProjectChanges changes)
    {
        var errors = new List<AuthorDiagnostic>();
        if (changes.Source.Identity != Source.Identity) errors.Add(new("project.identity", "Project, profile, mode or document generation changed"));
        foreach (var baseline in changes.Reads.Concat(changes.Files.Select(f => new FileBaseline(f.Path, f.Baseline))))
        {
            ProjectPaths.Validate(baseline.Path);
            var actual = files.TryGetValue(baseline.Path, out var data) ? Hash(data) : null;
            if (actual != baseline.Hash) errors.Add(new("project.conflict", "File baseline changed", baseline.Path));
        }
        foreach (var baseline in changes.Enumerations)
            if (!files.Keys.Where(p => p.StartsWith(baseline.Prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).SequenceEqual(baseline.Paths))
                errors.Add(new("project.enumeration", "Directory contents changed", baseline.Prefix));
        return errors.Distinct().ToArray();
    }
    public Dictionary<string, byte[]> CopyFiles() => files.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Version(IReadOnlyDictionary<string, byte[]> values) => Hash(JsonSerializer.SerializeToUtf8Bytes(
        values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new FileBaseline(p.Key, Hash(p.Value)))));
}

public static class ProjectPaths
{
    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
            path.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p.Equals(".bgb-author", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unsafe or reserved project path: " + path);
    }
    public static string Resolve(string root, string path)
    {
        Validate(path);
        root = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(root, path));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path escapes project: " + path);
        for (var current = result; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked project paths are unsupported: " + current);
        return result;
    }
    public static Dictionary<string, byte[]> Read(string root)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        Resolve(root, "game.start.json");
        Visit(root, "");
        return result;
        void Visit(string directory, string prefix)
        {
            foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (item.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) || item.Name.Equals(".bgb-author", StringComparison.OrdinalIgnoreCase)) continue;
                var path = prefix + item.Name;
                Resolve(root, path);
                if (item is DirectoryInfo) Visit(item.FullName, path + "/");
                else result.Add(path, File.ReadAllBytes(item.FullName));
            }
        }
    }
}
