using System.Text.Json;

namespace BoardGame.Authoring;

public sealed record AuthorPluginSelection(string Id, string Version = "1", int Api = 1);
public sealed record AuthorProfile(int Format, AuthorPluginSelection[] Plugins);
public sealed record EffectiveProfile(string Name, string Source, string Project, AuthorProfile Profile);

public static class AuthorProfiles
{
    public const string ConfigurationFile = "author.profile.json";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static AuthorProfile Default => new(1, [new("edit.disk"), new("author.files"), new("author.module"), new("author.state"), new("author.configuration"), new("author.resources"), new("author.validation"), new("author.run"), new("author.export")]);
    private sealed record Configuration(string? DefaultProfile, Dictionary<string, AuthorProfile>? Profiles);

    public static string Locate(string? explicitPath, string currentDirectory)
    {
        if (explicitPath != null)
        {
            var full = Path.GetFullPath(explicitPath, currentDirectory);
            return Path.GetFileName(full).Equals("game.start.json", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(full)! : full;
        }
        for (var directory = new DirectoryInfo(currentDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "game.start.json"))) return directory.FullName;
        throw new InvalidDataException("project.missing: pass --project to create or repair an incomplete project");
    }
    public static EffectiveProfile Resolve(string project, string? requested)
    {
        var path = Path.Combine(project, ConfigurationFile);
        var config = File.Exists(path) ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("profile.invalid: " + path) : null;
        var name = requested ?? config?.DefaultProfile;
        if (name == null) return new("builtin", "builtin", project, Default);
        if (config?.Profiles == null || !config.Profiles.TryGetValue(name, out var profile) || profile == null)
            throw new InvalidDataException("profile.missing: " + name);
        return new(name, requested == null ? "project-default" : "explicit", project, profile);
    }
}
