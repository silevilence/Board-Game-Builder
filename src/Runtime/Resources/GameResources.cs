using BoardGame.Kernel;

namespace BoardGame.Runtime;

// Resource-only validation uses the same runtime plugins as game loading, without requiring rules or a definition.
public static class GameResources
{
    public static ResourcePool Load(RuntimeStartup startup, string directory)
    {
        var selected = startup.Plugins.Where(p => ResourcePlugins.IsResource(p.Id)).ToList();
        if (selected.Count == 0) return ResourcePool.Empty;
        using var host = RuntimeHost.StartAsync(startup with { Plugins = selected }, ResourcePlugins.Catalog(() => null).ToArray(), ["resources"]).GetAwaiter().GetResult();
        return host.Service<ResourceRegistry>("resources").Load(new DirectoryResourceInput(directory));
    }
    public static string AtlasImage(byte[] json, string location)
    {
        using var document = TexturePackerFormat.Read(json, location);
        var image = document.RootElement.GetProperty("meta").GetProperty("image").GetString()!;
        ResourceSource.CheckPath(image);
        return image;
    }
}
