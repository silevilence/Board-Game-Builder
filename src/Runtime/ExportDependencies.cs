using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

public sealed record PlayerDependencyOptions(string? CacheDirectory = null, string? ComponentDirectory = null,
    bool AllowDownload = true, HttpClient? Client = null);
public sealed record ExportDependencyFile(string Path, long Size, string Sha256, string Component);
public sealed record ExportComponentSource(string Repository, string Tag, string Asset);
public sealed record ExportComponent(string Id, string Kind, string Identity, string[] Objects, long Size, string Sha256, ExportComponentSource Source);
public sealed record ExportDependencyManifest(int Api, string Format, string Version, string Platform,
    ExportDependencyFile[] Files, ExportComponent[] Components);

// Resolves redistributable Player bytes only from the locked component descriptor.
// Installed system frameworks are never used as export inputs.
public static class ExportDependencies
{
    public const string DescriptorName = "export.dependencies.json";

    public static string Resolve(string release, PlayerDependencyOptions? options = null,
        IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        ResolveAsync(Path.GetFullPath(release), options ?? new(), progress, cancellation).GetAwaiter().GetResult();

    private static async Task<string> ResolveAsync(string release, PlayerDependencyOptions options,
        IProgress<string>? progress, CancellationToken cancellation)
    {
        var descriptor = Path.Combine(release, DescriptorName);
        if (!File.Exists(descriptor)) return release;
        var bytes = await File.ReadAllBytesAsync(descriptor, cancellation).ConfigureAwait(false);
        using (var document = JsonDocument.Parse(bytes)) CheckDuplicateKeys(document.RootElement);
        var manifest = Wire.Read<ExportDependencyManifest>(System.Text.Encoding.UTF8.GetString(bytes));
        Validate(manifest);
        var identity = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var cache = Path.GetFullPath(options.CacheDirectory ?? DefaultCache(release));
        EnsureUnlinked(cache);
        Directory.CreateDirectory(cache);
        using var lease = await Acquire(Path.Combine(cache, "cache.lock"), cancellation).ConfigureAwait(false);
        var objects = Path.Combine(cache, "objects");
        var packages = Path.Combine(cache, "players");
        EnsureUnlinked(objects); EnsureUnlinked(packages);
        Directory.CreateDirectory(objects); Directory.CreateDirectory(packages);
        var prepared = Path.Combine(packages, identity);
        if (Directory.Exists(prepared))
        {
            VerifyFiles(prepared, manifest.Files);
            return prepared;
        }
        var needed = manifest.Files.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var file in needed.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            var stored = Path.Combine(objects, file.Sha256);
            if (File.Exists(stored)) { Verify(stored, file.Size, file.Sha256); continue; }
            var local = SafePath(release, file.Path);
            if (File.Exists(local) && new FileInfo(local).Length == file.Size && Hash(local) == file.Sha256)
                Link(stored, local);
        }
        using var ownedClient = options.Client == null ? new HttpClient { Timeout = TimeSpan.FromMinutes(5) } : null;
        var client = options.Client ?? ownedClient!;
        foreach (var component in manifest.Components)
        {
            var missing = needed.Values.Where(f => f.Component == component.Id && !File.Exists(Path.Combine(objects, f.Sha256))).ToArray();
            if (missing.Length == 0) continue;
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("获取并校验独立游戏运行依赖：" + component.Id);
            var transfer = Path.Combine(cache, "download-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                var localArchive = options.ComponentDirectory == null ? null : SafePath(Path.GetFullPath(options.ComponentDirectory), component.Source.Asset);
                if (localArchive != null && File.Exists(localArchive)) File.Copy(localArchive, transfer);
                else
                {
                    if (!options.AllowDownload)
                        throw new InvalidDataException("Offline export dependencies are missing: " + component.Id + ". Supply verified component archives or use the standalone toolkit.");
                    var source = component.Source;
                    var uri = new Uri($"https://github.com/{source.Repository}/releases/download/{source.Tag}/{source.Asset}");
                    try
                    {
                        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength is long length && length != component.Size)
                            throw new InvalidDataException("Component download size mismatch: " + component.Id);
                        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                        await WriteExact(input, transfer, component.Size, cancellation).ConfigureAwait(false);
                    }
                    catch (HttpRequestException error)
                    {
                        throw new IOException("Cannot obtain verified export dependencies. Connect to the release source, provide local component archives, or use the standalone toolkit. " + component.Id, error);
                    }
                }
                Verify(transfer, component.Size, component.Sha256);
                using var archive = ZipFile.OpenRead(transfer);
                var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
                if (names.Count != archive.Entries.Count || !names.SetEquals(component.Objects.Select(h => "objects/" + h)) ||
                    archive.Entries.Any(e => ((e.ExternalAttributes >> 16) & 0xf000) == 0xa000))
                    throw new InvalidDataException("Invalid component archive paths/links: " + component.Id);
                foreach (var file in missing)
                {
                    var entry = archive.GetEntry("objects/" + file.Sha256)!;
                    if (entry.Length != file.Size) throw new InvalidDataException("Component object size mismatch: " + file.Path);
                    var temporary = Path.Combine(objects, file.Sha256 + ".partial-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        await using (var input = entry.Open()) await WriteExact(input, temporary, file.Size, cancellation).ConfigureAwait(false);
                        Verify(temporary, file.Size, file.Sha256);
                        File.Move(temporary, Path.Combine(objects, file.Sha256));
                    }
                    finally { File.Delete(temporary); }
                }
            }
            finally { File.Delete(transfer); }
        }
        var staging = prepared + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = Path.Combine(objects, file.Sha256);
                Verify(source, file.Size, file.Sha256);
                var destination = SafePath(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Link(destination, source);
            }
            VerifyFiles(staging, manifest.Files);
            Directory.Move(staging, prepared);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        progress?.Report("已校验并缓存独立游戏运行依赖。");
        return prepared;
    }

    private static string DefaultCache(string release)
    {
        for (var directory = new DirectoryInfo(release); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "toolkit.json");
            if (!File.Exists(path)) continue;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.TryGetProperty("layout", out var layout) && layout.GetString() == "hardlinks-v1")
                return Path.Combine(directory.FullName, ".state", "export-dependencies");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoardGameBuilder", "export-dependencies");
    }

    private static void Validate(ExportDependencyManifest manifest)
    {
        if (manifest.Api != 1 || manifest.Format != "board-game-builder.player-components" || manifest.Platform != "win-x64" ||
            !Regex.IsMatch(manifest.Version ?? "", @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") ||
            manifest.Files is not { Length: > 0 and <= 20000 } || manifest.Components is not { Length: > 0 and <= 1000 })
            throw new InvalidDataException("Unknown or incomplete export dependency descriptor.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new Dictionary<string, ExportComponent>(StringComparer.Ordinal);
        var objects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            if (component is null || component.Source is null || component.Objects is not { Length: > 0 and <= 20000 } ||
                component.Kind is not ("runtime" or "dependency" or "product") || component.Id is null || !components.TryAdd(component.Id, component) ||
                !component.Id.StartsWith(component.Kind + "-", StringComparison.Ordinal) || !IsHash(component.Id[(component.Kind.Length + 1)..]) ||
                !IsHash(component.Sha256) || component.Size is <= 0 or > 2147483648 ||
                !Regex.IsMatch(component.Source.Repository ?? "", @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z") ||
                component.Source.Tag != "components-" + component.Id[(component.Kind.Length + 1)..] || component.Source.Asset != component.Id + ".zip" ||
                component.Objects.Any(h => !IsHash(h) || !objects.Add(h)))
                throw new InvalidDataException("Invalid immutable export component source.");
            var identity = JsonSerializer.Serialize(new { group = component.Identity, objects = component.Objects.Order(StringComparer.Ordinal) },
                new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            if (component.Identity is null || !component.Identity.StartsWith(component.Kind, StringComparison.Ordinal) ||
                component.Id != component.Kind + "-" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))))
                throw new InvalidDataException("Export component identity does not match its content: " + component.Id);
        }
        var identities = new Dictionary<string, (long Size, string Component)>();
        foreach (var file in manifest.Files)
        {
            if (file is null) throw new InvalidDataException("Missing export file descriptor.");
            ValidatePath(file.Path);
            if (!paths.Add(file.Path) || !IsHash(file.Sha256) || file.Size is < 0 or > 536870912 ||
                file.Component is null || !components.TryGetValue(file.Component, out var component) || !component.Objects.Contains(file.Sha256, StringComparer.Ordinal))
                throw new InvalidDataException("Unresolved or conflicting export file: " + file.Path);
            if (identities.TryGetValue(file.Sha256, out var previous) && previous != (file.Size, file.Component))
                throw new InvalidDataException("Conflicting export content identity.");
            identities[file.Sha256] = (file.Size, file.Component);
        }
        if (!paths.Contains("release.json") || !paths.Contains("Player.exe")) throw new InvalidDataException("Missing formal Player entries.");
        foreach (var path in paths)
            for (var parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)?.Replace('\\', '/'))
                if (paths.Contains(parent)) throw new InvalidDataException("Export file/directory collision: " + path);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static void ValidatePath(string path)
    {
        ModulePackage.ValidatePath(path);
        if (path.Split('/').Any(p => p.EndsWith(' ') || p.EndsWith('.') || p.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
            Regex.IsMatch(p, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe Windows export path: " + path);
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void Verify(string file, long size, string hash)
    {
        EnsureUnlinked(file);
        if (!File.Exists(file) || new FileInfo(file).Length != size || Hash(file) != hash)
            throw new InvalidDataException("Missing or corrupt export dependency: " + file);
    }
    private static void VerifyFiles(string root, IEnumerable<ExportDependencyFile> files)
    {
        EnsureUnlinked(root);
        foreach (var file in files) Verify(SafePath(root, file.Path), file.Size, file.Sha256);
    }
    private static string SafePath(string root, string relative)
    {
        ValidatePath(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Export dependency path escaped its root.");
        EnsureUnlinked(path);
        return path;
    }
    private static void EnsureUnlinked(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked export dependency paths are unsupported: " + current);
    }
    private static async Task<FileStream> Acquire(string path, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(100, cancellation).ConfigureAwait(false); }
        }
    }
    private static async Task WriteExact(Stream input, string target, long length, CancellationToken cancellation)
    {
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long written = 0; int count;
        while ((count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            written += count;
            if (written > length) throw new InvalidDataException("Component exceeded its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
        }
        if (written != length) throw new InvalidDataException("Incomplete component download.");
    }
    private static void Link(string destination, string source)
    {
        if (!OperatingSystem.IsWindows() || !CreateHardLink(ExtendedPath(destination), ExtendedPath(source), IntPtr.Zero))
            throw new IOException("Export dependency cache requires a writable, same-volume location supporting hard links. Choose a compatible cache directory. " + Marshal.GetLastWin32Error());
    }
    private static string ExtendedPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal)
        ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr attributes);
    private static void CheckDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate export descriptor field: " + property.Name);
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckDuplicateKeys(item);
    }
}
