using System.Reflection;
using System.Runtime.Loader;

namespace BoardGame.Authoring;

public interface IAuthorPluginPackage
{
    IReadOnlyList<AuthorPlugin> Plugins { get; }
}

public static class AuthorPluginPackages
{
    // Only explicitly installed author code is loaded here; game projects cannot select arbitrary DLL paths.
    public static IReadOnlyList<AuthorPlugin> Discover(string directory)
    {
        var plugins = AuthorHost.Builtins.ToList();
        if (!Directory.Exists(directory)) return plugins;
        foreach (var path in Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IAuthorPluginPackage).IsAssignableFrom(t)))
            {
                var package = (IAuthorPluginPackage)(Activator.CreateInstance(type) ?? throw new InvalidDataException("plugin.package: " + type.FullName));
                plugins.AddRange(package.Plugins);
            }
        }
        return plugins;
    }
}
