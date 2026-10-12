using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Win32;

internal static class SystemRuntime
{
    internal sealed record Framework(string Name, string Version, string Path);
    internal sealed record Resolution(string Root, IReadOnlyList<Framework> Frameworks);

    internal static Resolution Resolve(JsonElement program)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This toolkit requires Windows x64.");
        // .NET's globally registered location is explicitly in the 32-bit registry view.
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = registry.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
        var root = key?.GetValue("InstallLocation") as string;
        root = Path.GetFullPath(string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") : root);
        return ResolveAt(root, program);
    }

    internal static Resolution ResolveAt(string root, JsonElement program)
    {
        var host = Path.Combine(root, "dotnet.exe");
        RequireX64(host);
        var frameworks = new List<Framework>();
        foreach (var requirement in program.GetProperty("frameworks").EnumerateArray())
        {
            var name = requirement.GetProperty("framework").GetString()!;
            var minimum = Version.Parse(requirement.GetProperty("minimumVersion").GetString()!);
            if (name is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App") ||
                requirement.GetProperty("rollForward").GetString() != "LatestPatch")
                throw new InvalidDataException("Unsupported system runtime policy.");
            var folder = Path.Combine(root, "shared", name);
            var choices = Directory.Exists(folder) ? Directory.GetDirectories(folder)
                .Select(path => (Path: path, Name: Path.GetFileName(path)))
                .Where(p => p.Name.Split('.').Length == 3 && Version.TryParse(p.Name, out _))
                .Select(p => (p.Path, Version: Version.Parse(p.Name)))
                .Where(p => p.Version.Major == minimum.Major && p.Version.Minor == minimum.Minor && p.Version >= minimum)
                .OrderByDescending(p => p.Version).ToArray() : [];
            if (choices.Length == 0)
                throw new InvalidDataException($"Missing compatible x64 {name} {minimum} (same major/minor, newer patch allowed) at {root}. Install that .NET Runtime, or use the standalone package. No SDK is required.");
            var selected = choices[0];
            if (!File.Exists(Path.Combine(selected.Path, name + ".deps.json")))
                throw new InvalidDataException("System runtime installation is incomplete: " + selected.Path);
            if (name == "Microsoft.NETCore.App") RequireX64(Path.Combine(selected.Path, "coreclr.dll"));
            if (name == "Microsoft.WindowsDesktop.App") RequireX64(Path.Combine(selected.Path, "PresentationNative_cor3.dll"));
            frameworks.Add(new(name, selected.Version.ToString(), selected.Path));
        }
        if (frameworks.All(f => f.Name != "Microsoft.NETCore.App") || frameworks.Select(f => f.Name).Distinct().Count() != frameworks.Count)
            throw new InvalidDataException("Missing or duplicate runtime framework requirements.");
        var core = frameworks.Single(f => f.Name == "Microsoft.NETCore.App");
        foreach (var desktop in frameworks.Where(f => f.Name == "Microsoft.WindowsDesktop.App"))
        {
            using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(desktop.Path, desktop.Name + ".runtimeconfig.json")));
            var dependency = config.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
            var minimum = Version.Parse(dependency.GetProperty("version").GetString()!);
            var actual = Version.Parse(core.Version);
            if (dependency.GetProperty("name").GetString() != core.Name || actual.Major != minimum.Major || actual.Minor != minimum.Minor || actual < minimum)
                throw new InvalidDataException($"Installed {desktop.Name} {desktop.Version} requires compatible x64 {core.Name} {minimum}. Repair the .NET Desktop Runtime installation.");
        }
        return new(root, frameworks);
    }

    internal static void Apply(ProcessStartInfo start, Resolution resolution)
    {
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("DOTNET_ROLL_FORWARD", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["DOTNET_ROOT"] = resolution.Root;
        start.Environment["DOTNET_ROOT_X64"] = resolution.Root;
        start.Environment["DOTNET_ROLL_FORWARD"] = "LatestPatch";
        start.Environment["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "0";
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
    }

    private static void RequireX64(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("A compatible x64 .NET Runtime is required. Install it, or use the standalone package. Missing: " + path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
            throw new InvalidDataException("Wrong .NET Runtime architecture; Windows x64 is required: " + path);
    }
}
