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
            var fullPath = Path.GetFullPath(path);
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IAuthorPluginPackage).IsAssignableFrom(t)))
                {
                    var package = (IAuthorPluginPackage)(Activator.CreateInstance(type) ?? throw new InvalidDataException("plugin.package: " + type.FullName));
                    plugins.AddRange(package.Plugins);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                var detail = error is ReflectionTypeLoadException types
                    ? string.Join("; ", types.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct())
                    : error.GetBaseException().Message;
                throw new AuthorOperationException("plugin.load", "Cannot load author plugin package: " + fullPath + ": " + detail, fullPath);
            }
        }
        return plugins;
    }
}
