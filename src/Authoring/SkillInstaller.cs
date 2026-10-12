using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace BoardGame.Authoring;

public sealed record SkillInstall(string Product, string Id, string Cli, string Docs, string Generated, string Method, string Hash, string Version, string? Name = null, string[]? CommandArguments = null);
public sealed record SkillInstallStatus(string Target, bool Installed, bool Healthy, SkillInstall? Installation, string[] Diagnostics);
public static class SkillInstaller
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) {WriteIndented=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    private static string Hash(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    private static string ValidateName(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,@"\Abgb-author(?:-v[0-9]+-[0-9]+-[0-9]+)?\z")?name:throw new ArgumentException("Invalid author skill identity");
    private const string Marker = "bgb-install.json";
    private const string Product = "bgb-author-v1";
    private static string GeneratedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoardGameBuilder", "author", "skills");
    private static string Target(string parent,string name) => Path.Combine(Path.GetFullPath(parent), ValidateName(name));
    private static string Ledger(string parent,string name) => Path.Combine(Path.GetFullPath(parent), "."+ValidateName(name)+"-install.json");
    private static SkillInstall? Read(string parent,string name)
    {
        var ledger = Ledger(parent,name); var marker = Path.Combine(Target(parent,name), Marker);
        var file = File.Exists(ledger) ? ledger : File.Exists(marker) ? marker : null;
        if (file == null) return null;
        var value = JsonSerializer.Deserialize<SkillInstall>(File.ReadAllText(file), Json);
        if ((value?.Name??"bgb-author")!=name || value?.Product != Product || !Guid.TryParseExact(value.Id, "N", out _) || value.Method is not ("copy" or "junction") ||
            !Path.GetFullPath(value.Generated).StartsWith(Path.TrimEndingDirectorySeparator(GeneratedRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("skill.owner: unrecognized installation metadata");
        return value;
    }
    public static SkillInstallStatus Status(string parent,string name="bgb-author")
    {
        var target = Target(parent,name); var installation = Read(parent,name); var errors = new List<string>();
        if (installation == null) return new(target, false, false, null, Directory.Exists(target) ? ["Destination belongs to another skill"] : []);
        if (!File.Exists(installation.Cli)) errors.Add("CLI moved or missing; run repair from the current installation");
        if (!File.Exists(Path.Combine(installation.Docs, "entry.md"))) errors.Add("Author documents moved or missing");
        var skill = Path.Combine(target, "SKILL.md");
        if (!File.Exists(skill)) errors.Add("Rendered SKILL.md is missing");
        else if (Hash(File.ReadAllBytes(skill)) != installation.Hash) errors.Add("Installed SKILL.md was modified; preserve edits before replacing it");
        if (installation.Method == "junction" && (!IsLink(target) || !string.Equals(new DirectoryInfo(target).ResolveLinkTarget(false)?.FullName, installation.Generated, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Junction does not point to the owned generated skill");
        if (installation.Method == "copy" && IsLink(target)) errors.Add("Copy installation unexpectedly became a link");
        return new(target, true, errors.Count == 0, installation, errors.ToArray());
    }
    public static SkillInstallStatus Install(string parent, string cli, string docs, string template, string method = "copy", bool repair = false,string name="bgb-author",string? version=null,string[]? arguments=null)
    {
        if (method is not ("copy" or "junction")) throw new ArgumentException("Skill method must be copy or junction");
        if (method == "junction" && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Junction installation requires Windows");
        var rendered = AuthorSkillTemplate.Render(template, cli, docs,arguments,name);
        parent = Path.GetFullPath(parent); cli = Path.GetFullPath(cli); docs = Path.GetFullPath(docs);
        var target = Target(parent,name); var previous = Read(parent,name);
        if (previous == null && (Directory.Exists(target) || File.Exists(target) || IsLink(target))) throw new IOException("skill.conflict: existing skill is not owned by this installer");
        if (previous != null)
        {
            var samePinnedRoute=repair&&name!="bgb-author"&&previous.Version==version&&previous.Docs.Equals(docs,StringComparison.OrdinalIgnoreCase)&&(previous.CommandArguments??[]).SequenceEqual(arguments??[]);
            if (!previous.Cli.Equals(cli, StringComparison.OrdinalIgnoreCase) && (!repair || File.Exists(previous.Cli))&&!samePinnedRoute)
                throw new IOException("skill.instance: another product installation owns this target; choose a different skill directory");
            ValidateOwned(target, previous, allowMissing: repair);
        }
        Directory.CreateDirectory(parent);
        var id = Guid.NewGuid().ToString("N"); var generated = Path.Combine(GeneratedRoot, id, name);
        Directory.CreateDirectory(generated);
        var bytes = new UTF8Encoding(false).GetBytes(rendered);
        var installation = new SkillInstall(Product, id, cli, docs, generated, method, Hash(bytes), version??typeof(SkillInstaller).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion??"unknown",name=="bgb-author"?null:name,arguments);
        var metadata = JsonSerializer.Serialize(installation, Json);
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
            AtomicTextFile.Write(Ledger(parent,name), metadata, true);
            committed = true;
            if (backedUp) RemoveOwned(backup, previous!);
            if (previous != null) RemoveGenerated(previous);
            return Status(parent,name);
        }
        catch
        {
            if (committed) throw;
            if (published) RemoveOwned(target, installation);
            published = false;
            if (backedUp && (Directory.Exists(backup) || IsLink(backup))) Directory.Move(backup, target);
            if (previous != null) AtomicTextFile.Write(Ledger(parent,name), JsonSerializer.Serialize(previous, Json), true);
            else if (File.Exists(Ledger(parent,name))) File.Delete(Ledger(parent,name));
            throw;
        }
        finally
        {
            if (Directory.Exists(staging) || IsLink(staging)) RemoveOwned(staging, installation);
            if (!published) RemoveGenerated(installation);
        }
    }
    public static SkillInstallStatus Uninstall(string parent,string name="bgb-author")
    {
        var installation = Read(parent,name) ?? throw new IOException("skill.owner: no owned installation found");
        var target = Target(parent,name); ValidateOwned(target, installation, true);
        if (Directory.Exists(target) || IsLink(target)) RemoveOwned(target, installation);
        if (File.Exists(Ledger(parent,name))) File.Delete(Ledger(parent,name));
        RemoveGenerated(installation);
        return Status(parent,name);
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
        if (File.Exists(skill) && Hash(File.ReadAllBytes(skill)) != installation.Hash) throw new IOException("skill.modified: installed skill has user changes");
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
        if (path != Path.Combine(GeneratedRoot, installation.Id, installation.Name??"bgb-author") || IsLink(path) || IsLink(Path.GetDirectoryName(path)!)) throw new IOException("skill.owner: generated path is not owned");
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
