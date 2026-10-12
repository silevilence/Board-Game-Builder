using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Native AOT bootstrap: no engine, UI framework or installed .NET dependency.
// Each public executable has its own native assembly identity and launch target.
try
{
    var executable = Path.GetFullPath(Environment.ProcessPath!);
    var root = FindRoot(Path.GetDirectoryName(executable)!);
    var manifestPath = Path.Combine(root, "toolkit.json");
    using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
    var manifestRoot = manifest.RootElement;
    if (manifestRoot.GetProperty("api").GetInt32() != 2 ||
        manifestRoot.GetProperty("format").GetString() != "board-game-builder.toolkit" ||
        manifestRoot.GetProperty("platform").GetString() != "win-x64" ||
        manifestRoot.GetProperty("layout").GetString() != "hardlinks-v1")
        throw new InvalidDataException("Unsupported toolkit layout. Install a compatible complete toolkit.");
    var entry = Path.GetRelativePath(root, executable).Replace('\\', '/');
    var target = SafePath(root, manifestRoot.GetProperty("entryTargets").GetProperty(entry).GetString()!);
    var state = Path.Combine(root, ".state");
    RejectLinks(root, state);
    Directory.CreateDirectory(state);
    using (var gate = Acquire(Path.Combine(state, "layout.lock")))
    {
        Materialize(root, manifestRoot.GetProperty("files"));
        if (!manifestRoot.GetProperty("files").EnumerateArray().Any(f => SafePath(root, f.GetProperty("path").GetString()!) == target))
            throw new InvalidDataException("Selected entry is not listed in the product manifest.");
    }
    var start = new ProcessStartInfo(target)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var argument in args) start.ArgumentList.Add(argument);
    // Preserve the caller's working directory and standard handles (CLI / Lua pipes).
    using var child = Process.Start(start) ?? throw new IOException("Cannot start the selected tool.");
    using var inputLifetime = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        try
        {
            await Console.OpenStandardInput().CopyToAsync(child.StandardInput.BaseStream, inputLifetime.Token);
            child.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    });
    var output = child.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
    var errors = child.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
    child.WaitForExit();
    inputLifetime.Cancel();
    Task.WhenAll(output, errors).GetAwaiter().GetResult();
    return child.ExitCode;
}
catch (Exception error)
{
    var message = "Board Game Builder: " + error.Message;
    var errorHandle = Native.GetStdHandle(-12);
    if (Native.GetConsoleWindow() != IntPtr.Zero ||
        (errorHandle != IntPtr.Zero && errorHandle != new IntPtr(-1) && Native.GetFileType(errorHandle) != 0))
        Console.Error.WriteLine(message);
    else
        Native.MessageBox(IntPtr.Zero, message, "Board Game Builder — startup failed", 0x10);
    return 1;
}

static string FindRoot(string directory)
{
    for (var path = new DirectoryInfo(directory); path != null; path = path.Parent)
        if (File.Exists(Path.Combine(path.FullName, "toolkit.json"))) return path.FullName;
    throw new FileNotFoundException("toolkit.json is missing. Extract the complete toolkit ZIP.");
}

static FileStream Acquire(string path)
{
    var deadline = Stopwatch.StartNew();
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(60)) { Thread.Sleep(100); }
    }
}

static string SafePath(string root, string relative)
{
    if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\')) throw new InvalidDataException("Invalid layout path.");
    if (relative.Equals("toolkit.json", StringComparison.OrdinalIgnoreCase) || relative.Split('/')[0].Equals(".state", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Product files cannot replace the manifest or writable state.");
    foreach (var part in relative.Split('/'))
    {
        var stem = part.Split('.')[0];
        if (part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
            new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
            throw new InvalidDataException("Unsafe layout path: " + relative);
    }
    var full = Path.GetFullPath(Path.Combine(root, relative));
    if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Layout escaped installation directory.");
    RejectLinks(root, full);
    return full;
}

static void RejectLinks(string root, string path)
{
    for (var current = path; current.Length >= root.Length; current = Path.GetDirectoryName(current)!)
    {
        if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked directories and external links are not supported: " + current);
        if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
    }
}

static string Hash(string file)
{
    using var stream = File.OpenRead(file);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

static void Materialize(string root, JsonElement files)
{
    var entries = new Dictionary<string, (string Storage, long Size, string Hash)>(StringComparer.OrdinalIgnoreCase);
    var storage = new Dictionary<string, (long Size, string Hash)>(StringComparer.OrdinalIgnoreCase);
    var content = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var file in files.EnumerateArray())
    {
        var path = SafePath(root, file.GetProperty("path").GetString()!);
        var source = SafePath(root, file.GetProperty("storage").GetString()!);
        var size = file.GetProperty("size").GetInt64();
        var hash = file.GetProperty("sha256").GetString()!;
        if (size < 0 || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitLower(c)) ||
            !entries.TryAdd(path, (source, size, hash))) throw new InvalidDataException("Invalid/duplicate content identity.");
        if (storage.TryGetValue(source, out var prior) && prior != (size, hash))
            throw new InvalidDataException("Conflicting stored content.");
        if (content.TryGetValue(hash, out var previous) && !previous.Equals(source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Duplicate physical content in layout.");
        storage[source] = (size, hash);
        content[hash] = source;
    }
    foreach (var item in storage)
    {
        if (!entries.TryGetValue(item.Key, out var owner) || !owner.Storage.Equals(item.Key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Storage must refer directly to a canonical product file.");
        var source = new FileInfo(item.Key);
        if (!source.Exists || source.Length != item.Value.Size || Hash(item.Key) != item.Value.Hash)
            throw new InvalidDataException("Product content is missing or corrupt: " + item.Key + ". Import the original ZIP to repair it.");
    }
    // Probe hard-link support before creating any product aliases. Never silently copy.
    var probe = Path.Combine(root, ".state", "probe-" + Guid.NewGuid().ToString("N"));
    var link = probe + ".link";
    try
    {
        File.WriteAllText(probe, "hard-link probe");
        if (!Native.CreateHardLink(link, probe, IntPtr.Zero))
            throw new IOException("This installation location does not support hard links. Extract to a writable local NTFS volume. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }
    finally { File.Delete(link); File.Delete(probe); }
    foreach (var item in entries)
    {
        if (item.Key.Equals(item.Value.Storage, StringComparison.OrdinalIgnoreCase)) continue;
        if (File.Exists(item.Key))
        {
            if (!SameFile(item.Key, item.Value.Storage))
                throw new IOException("Existing file is not a shared product link; re-extract into an empty directory: " + item.Key);
            continue;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);
        if (!Native.CreateHardLink(item.Key, item.Value.Storage, IntPtr.Zero))
            throw new IOException("Cannot create shared product link: " + item.Key + ". " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }
}

static bool SameFile(string left, string right)
{
    using var a = File.OpenHandle(left);
    using var b = File.OpenHandle(right);
    if (!Native.GetFileInformationByHandle(a, out var x) || !Native.GetFileInformationByHandle(b, out var y))
        throw new IOException("Cannot inspect shared file identity.");
    return x.VolumeSerialNumber == y.VolumeSerialNumber && x.FileIndexHigh == y.FileIndexHigh && x.FileIndexLow == y.FileIndexLow;
}

internal static partial class Native
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out FileInformation info);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetStdHandle(int identifier);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetFileType(IntPtr handle);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(IntPtr window, string text, string caption, uint type);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        internal uint Attributes;
        internal uint CreationTimeLow, CreationTimeHigh, AccessTimeLow, AccessTimeHigh, WriteTimeLow, WriteTimeHigh;
        internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}
