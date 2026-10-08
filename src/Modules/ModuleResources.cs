using BoardGame.Kernel;

namespace BoardGame.Modules;

public static class ModuleResources
{
    public static SortedDictionary<string,byte[]> Load(string directory,IEnumerable<KeyValuePair<string,string>> declarations)
    {
        var data=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var entry in declarations)
        {
            var bytes=File.ReadAllBytes(DirectoryModule.SafeFile(directory,entry.Value));
            if(!data.TryAdd(entry.Key,bytes))throw new InvalidDataException("Conflicting resource key: "+entry.Key);
            using var atlas=ImageCatalog.ReadAtlas(bytes,entry.Value);
            if(atlas==null)continue;
            var image=ImagePath(entry.Value,atlas.RootElement.GetProperty("meta").GetProperty("image").GetString()!);
            if(!data.TryAdd(ImageCatalog.ImageKey(entry.Key),File.ReadAllBytes(DirectoryModule.SafeFile(directory,image))))
                throw new InvalidDataException("Conflicting atlas image key: "+entry.Key);
        }
        _=new ImageCatalog(data);
        return data;
    }
    public static IEnumerable<string> Files(string directory,IEnumerable<string> paths)
    {
        foreach(var path in paths)
        {
            yield return path;
            using var atlas=ImageCatalog.ReadAtlas(File.ReadAllBytes(DirectoryModule.SafeFile(directory,path)),path);
            if(atlas!=null)yield return ImagePath(path,atlas.RootElement.GetProperty("meta").GetProperty("image").GetString()!);
        }
    }
    private static string ImagePath(string atlas,string image)
    {
        ModulePackage.ValidatePath(image);
        var path=Path.Combine(Path.GetDirectoryName(atlas) ?? "",image).Replace('\\','/');
        ModulePackage.ValidatePath(path);return path;
    }
}
