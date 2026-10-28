using BoardGame.Kernel;
using BoardGame.Resources;

namespace BoardGame.Runtime;

public interface IResourceLoader
{
    void Load(ResourceSource source,ResourceInput input,AssetBuilder builder);
}
internal sealed class BinaryResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,AssetBuilder builder)
    {foreach(var file in input.Match(source)) {input.Read(file.Path);builder.Binary(file.Name,file.Path);}}
}
internal sealed class ImageResourceLoader : IResourceLoader
{
    public void Load(ResourceSource source,ResourceInput input,AssetBuilder builder)
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
    public void Load(ResourceSource source,ResourceInput input,AssetBuilder builder)
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
