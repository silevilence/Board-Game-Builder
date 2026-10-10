using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace BoardGame.Authoring;

public sealed record SkillInstall(string Product, string Id, string Cli, string Docs, string Generated, string Method, string Hash, string Version);
public sealed record SkillInstallStatus(string Target, bool Installed, bool Healthy, SkillInstall? Installation, string[] Diagnostics);
public static class SkillInstaller
{
    private const string Marker = "bgb-install.json";
    private const string Product = "bgb-author-v1";
    private static string GeneratedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoardGameBuilder", "author", "skills");
    private static string Target(string parent) => Path.Combine(Path.GetFullPath(parent), "bgb-author");
    private static string Ledger(string parent) => Path.Combine(Path.GetFullPath(parent), ".bgb-author-install.json");
    private static SkillInstall? Read(string parent)
    {
        var ledger = Ledger(parent); var marker = Path.Combine(Target(parent), Marker);
        var file = File.Exists(ledger) ? ledger : File.Exists(marker) ? marker : null;
        if (file == null) return null;
        var value = JsonSerializer.Deserialize<SkillInstall>(File.ReadAllText(file), AuthorProfiles.Json);
        if (value?.Product != Product || !Guid.TryParseExact(value.Id, "N", out _) || value.Method is not ("copy" or "junction") ||
            !Path.GetFullPath(value.Generated).StartsWith(Path.TrimEndingDirectorySeparator(GeneratedRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("skill.owner: unrecognized installation metadata");
        return value;
    }
    public static SkillInstallStatus Status(string parent)
    {
        var target = Target(parent); var installation = Read(parent); var errors = new List<string>();
        if (installation == null) return new(target, false, false, null, Directory.Exists(target) ? ["Destination belongs to another skill"] : []);
        if (!File.Exists(installation.Cli)) errors.Add("CLI moved or missing; run repair from the current installation");
        if (!File.Exists(Path.Combine(installation.Docs, "entry.md"))) errors.Add("Author documents moved or missing");
        var skill = Path.Combine(target, "SKILL.md");
        if (!File.Exists(skill)) errors.Add("Rendered SKILL.md is missing");
        else if (ProjectView.Hash(File.ReadAllBytes(skill)) != installation.Hash) errors.Add("Installed SKILL.md was modified; preserve edits before replacing it");
        if (installation.Method == "junction" && (!IsLink(target) || !string.Equals(new DirectoryInfo(target).ResolveLinkTarget(false)?.FullName, installation.Generated, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Junction does not point to the owned generated skill");
        if (installation.Method == "copy" && IsLink(target)) errors.Add("Copy installation unexpectedly became a link");
        return new(target, true, errors.Count == 0, installation, errors.ToArray());
    }
    public static SkillInstallStatus Install(string parent, string cli, string docs, string template, string method = "copy", bool repair = false)
    {
        if (method is not ("copy" or "junction")) throw new ArgumentException("Skill method must be copy or junction");
        if (method == "junction" && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Junction installation requires Windows");
        var rendered = AuthorSkillTemplate.Render(template, cli, docs);
        parent = Path.GetFullPath(parent); cli = Path.GetFullPath(cli); docs = Path.GetFullPath(docs);
        var target = Target(parent); var previous = Read(parent);
        if (previous == null && (Directory.Exists(target) || File.Exists(target) || IsLink(target))) throw new IOException("skill.conflict: existing skill is not owned by this installer");
        if (previous != null)
        {
            if (!previous.Cli.Equals(cli, StringComparison.OrdinalIgnoreCase) && (!repair || File.Exists(previous.Cli)))
                throw new IOException("skill.instance: another product installation owns this target; choose a different skill directory");
            ValidateOwned(target, previous, allowMissing: repair);
        }
        Directory.CreateDirectory(parent);
        var id = Guid.NewGuid().ToString("N"); var generated = Path.Combine(GeneratedRoot, id, "bgb-author");
        Directory.CreateDirectory(generated);
        var bytes = new UTF8Encoding(false).GetBytes(rendered);
        var installation = new SkillInstall(Product, id, cli, docs, generated, method, ProjectView.Hash(bytes), AuthorDocuments.Version);
        var metadata = JsonSerializer.Serialize(installation, AuthorProfiles.Json);
        File.WriteAllBytes(Path.Combine(generated, "SKILL.md"), bytes); File.WriteAllText(Path.Combine(generated, Marker), metadata);
        var staging = target + ".install-" + id; var backup = target + ".backup-" + id;
        var backedUp = false; var published = false; var committed = false;
        try
        {
            if (method == "junction") Junction.Create(staging, generated);
            else
            {
                Directory.CreateDirectory(staging);
                File.Copy(Path.Combine(generated, "SKILL.md"), Path.Combine(staging, "SKILL.md"));
                File.Copy(Path.Combine(generated, Marker), Path.Combine(staging, Marker));
            }
            if (Directory.Exists(target) || IsLink(target)) { Directory.Move(target, backup); backedUp = true; }
            Directory.Move(staging, target); published = true;
            AtomicTextFile.Write(Ledger(parent), metadata, true);
            committed = true;
            if (backedUp) RemoveOwned(backup, previous!);
            if (previous != null) RemoveGenerated(previous);
            return Status(parent);
        }
        catch
        {
            if (committed) throw;
            if (published) RemoveOwned(target, installation);
            published = false;
            if (backedUp && (Directory.Exists(backup) || IsLink(backup))) Directory.Move(backup, target);
            if (previous != null) AtomicTextFile.Write(Ledger(parent), JsonSerializer.Serialize(previous, AuthorProfiles.Json), true);
            else if (File.Exists(Ledger(parent))) File.Delete(Ledger(parent));
            throw;
        }
        finally
        {
            if (Directory.Exists(staging) || IsLink(staging)) RemoveOwned(staging, installation);
            if (!published) RemoveGenerated(installation);
        }
    }
    public static SkillInstallStatus Uninstall(string parent)
    {
        var installation = Read(parent) ?? throw new IOException("skill.owner: no owned installation found");
        var target = Target(parent); ValidateOwned(target, installation, true);
        if (Directory.Exists(target) || IsLink(target)) RemoveOwned(target, installation);
        if (File.Exists(Ledger(parent))) File.Delete(Ledger(parent));
        RemoveGenerated(installation);
        return Status(parent);
    }
    private static bool IsLink(string path) => new DirectoryInfo(path).LinkTarget != null;
    private static void ValidateOwned(string path, SkillInstall installation, bool allowMissing)
    {
        if (IsLink(path))
        {
            if (installation.Method != "junction" || !string.Equals(new DirectoryInfo(path).ResolveLinkTarget(false)?.FullName, installation.Generated, StringComparison.OrdinalIgnoreCase))
                throw new IOException("skill.owner: unexpected link target");
        }
        else if (!Directory.Exists(path)) { if (allowMissing) return; throw new IOException("skill.missing: installation directory missing"); }
        foreach (var file in Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path) : [])
            if (Path.GetFileName(file) is not ("SKILL.md" or Marker) || Directory.Exists(file)) throw new IOException("skill.conflict: unowned content in skill directory");
        var skill = Path.Combine(path, "SKILL.md");
        if (File.Exists(skill) && ProjectView.Hash(File.ReadAllBytes(skill)) != installation.Hash) throw new IOException("skill.modified: installed skill has user changes");
    }
    private static void RemoveOwned(string path, SkillInstall installation)
    {
        ValidateOwned(path, installation, true);
        if (IsLink(path)) { Directory.Delete(path); return; }
        if (!Directory.Exists(path)) return;
        foreach (var name in new[] { "SKILL.md", Marker }) { var file = Path.Combine(path, name); if (File.Exists(file)) File.Delete(file); }
        Directory.Delete(path); // Never recursively delete an installation or a link target.
    }
    private static void RemoveGenerated(SkillInstall installation)
    {
        var path = Path.GetFullPath(installation.Generated);
        if (path != Path.Combine(GeneratedRoot, installation.Id, "bgb-author") || IsLink(path) || IsLink(Path.GetDirectoryName(path)!)) throw new IOException("skill.owner: generated path is not owned");
        if (!Directory.Exists(path)) return;
        RemoveOwned(path, installation with { Method = "copy" });
        Directory.Delete(Path.GetDirectoryName(path)!);
    }
}

internal static class Junction
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int length, IntPtr output, int outputLength, out int returned, IntPtr overlapped);
    public static void Create(string path, string target)
    {
        Directory.CreateDirectory(path);
        using var handle = CreateFile(path, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + Path.GetFullPath(target)); var print = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
        var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0); BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10); BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14); substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 18 + substitute.Length);
        if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
