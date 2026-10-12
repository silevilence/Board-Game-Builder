namespace BoardGame.Launcher;

internal static class ManifestCache
{
    internal static void Write(string home,ProductManifest manifest)
    {
        var path=ProductManifest.SafePath(home,"manifests/"+manifest.Digest+".json");
        if(File.Exists(path)&&ProductManifest.HashFile(path)==manifest.Digest) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temporary,manifest.Bytes);File.Move(temporary,path,true);} finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
    internal static ProductManifest Read(InstallationRegistry registry,InstalledVersion item)
    {
        ProductManifest? manifest=null;
        try {manifest=ProductManifest.Read(ProductManifest.SafePath(item.Directory,"toolkit.json"));if(manifest.Digest==item.ManifestHash) {Write(registry.Home,manifest);return manifest;}}
        catch(Exception e) when(e is IOException or InvalidDataException) { }
        manifest=ProductManifest.Read(ProductManifest.SafePath(registry.Home,"manifests/"+item.ManifestHash+".json"));
        if(manifest.Digest!=item.ManifestHash || manifest.Version!=item.Version || manifest.Mode!=item.Mode) throw new InvalidDataException("保存的安装身份已损坏，停止维护。");
        return manifest;
    }
}
