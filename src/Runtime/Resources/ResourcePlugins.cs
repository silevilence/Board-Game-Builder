using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal interface IResourceLoader
{
    void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder);
}
internal sealed class ResourcePoolBuilder
{
    private readonly Dictionary<string,ImageReference> images=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> binaries=new(StringComparer.Ordinal);
    private readonly HashSet<string> names=new(StringComparer.Ordinal);
    private void Reserve(string name)
    {ResourceSource.CheckName(name);if(!names.Add(name))throw new InvalidDataException("duplicate/conflicting resource name: "+name);if(names.Count>16384)throw new InvalidDataException("Resource name budget exceeded");}
    public void Image(string name,ImageReference image) {Reserve(name);images.Add(name,image);}
    public void Binary(string name,string path) {Reserve(name);binaries.Add(name,path);}
    public ResourcePool Freeze(ResourceInput input)=>new(input.Files,images,binaries);
}
internal sealed class ResourceRegistry(ResourcePool? existing=null)
{
    private readonly List<(IResourceLoader Loader,ResourceSource[] Sources)> registrations=[];
    public ResourcePool Pool {get;private set;}=existing ?? ResourcePool.Empty;
    public IDisposable Register(IResourceLoader loader,ResourceSource[] sources)
    {
        var entry=(loader,sources);registrations.Add(entry);return new Registration(()=>registrations.Remove(entry));
    }
    public ResourcePool Load(ResourceInput input)
    {
        var builder=new ResourcePoolBuilder();
        foreach(var entry in registrations)foreach(var source in entry.Sources)entry.Loader.Load(source,input,builder);
        return Pool=builder.Freeze(input);
    }
    private sealed class Registration(Action release) : IDisposable
    {private bool disposed;public void Dispose() {if(disposed)return;disposed=true;release();}}
}
internal sealed class BinaryResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder)
    {foreach(var file in input.Match(source)) {input.Read(file.Path);builder.Binary(file.Name,file.Path);}}
}
internal sealed class ImageResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder)
    {
        foreach(var file in input.Match(source,path=>Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"))
        {
            var bytes=input.Read(file.Path);_=ImageFile.Size(bytes,file.Path);
            builder.Image(file.Name,new(file.Path));
        }
    }
}
internal sealed class TexturePackerResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,ResourcePoolBuilder builder)
    {
        if(source.Name!=null)throw new InvalidDataException("TexturePacker names come from frames; use prefix instead of name");
        foreach(var file in input.Match(source,path=>Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)))
        {
            using var json=TexturePackerFormat.Read(input.Read(file.Path),file.Path);
            var image=json.RootElement.GetProperty("meta").GetProperty("image").GetString()!;ResourceSource.CheckPath(image);
            var path=Path.Combine(Path.GetDirectoryName(file.Path) ?? "",image).Replace('\\','/');
            foreach(var frame in TexturePackerFormat.Frames(json,path,input.Read(path),file.Path))builder.Image(source.Prefix+frame.Key,frame.Value);
        }
    }
}
internal static class ResourcePlugins
{
    internal static bool IsResource(string id)=>id is "resources" or "resources.images" or "resources.texturepacker";
    public static IEnumerable<RuntimePluginDefinition> Catalog(Func<ResourcePool?> pool)
    {
        yield return new("resources","1",1,["resources"],[],scope=>
        {
            var registry=new ResourceRegistry(pool());scope.Provide("resources",registry);
            scope.Own(registry.Register(new BinaryResourceLoader(),ResourceSource.Read(scope.Configuration,"resources")));
        });
        yield return Loader("resources.images",new ImageResourceLoader());
        yield return Loader("resources.texturepacker",new TexturePackerResourceLoader());
    }
    private static RuntimePluginDefinition Loader(string id,IResourceLoader loader)=>new(id,"1",1,[id],["resources"],scope=>
    {
        var registration=scope.Service<ResourceRegistry>("resources").Register(loader,ResourceSource.Read(scope.Configuration,id));
        scope.Provide(id,scope.Own(registration));
    });
}
