using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record InstalledVersion(string Version,string Directory,string Mode,string ManifestHash,bool Managed=false);
public sealed record LauncherSettings(int Api,string StorageRoot,string? DefaultVersion,InstalledVersion[] Installations);
public sealed record InstallationStatus(InstalledVersion Installation,string State,string? Diagnostic);

/// <summary>Durable per-user settings; engine files and projects are never settings storage.</summary>
public sealed class InstallationRegistry
{
    public string Home { get; }
    public static string DefaultHome => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","Launcher");
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { WriteIndented=true, PropertyNameCaseInsensitive=false, UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public InstallationRegistry(string? home=null)
    {
        Home=Path.GetFullPath(home??DefaultHome);
        ProductManifest.SafePath(Home,"settings.json"); Directory.CreateDirectory(Home);
        using var gate=Lock();
        if(!File.Exists(Path.Combine(Home,"settings.json"))) Write(new(1,Path.Combine(Home,"engine-storage"),null,[]));
        _=Read();
    }
    public LauncherSettings Read()
    {
        var bytes=File.ReadAllBytes(ProductManifest.SafePath(Home,"settings.json"));
        if(bytes.Length>4*1024*1024) throw new InvalidDataException("启动器登记文件过大。");
        using var document=JsonDocument.Parse(bytes); ProductManifest.Unique(document.RootElement);
        var result=JsonSerializer.Deserialize<LauncherSettings>(bytes,Json)??throw new InvalidDataException("启动器登记文件为空。");
        if(result.Api!=1 || !Path.IsPathFullyQualified(result.StorageRoot) || result.Installations==null || result.Installations.Length>1000) throw new InvalidDataException("不支持的启动器登记格式。");
        var versions=new HashSet<string>();
        foreach(var item in result.Installations)
            if(item==null || ProductManifest.NormalizeVersion(item.Version)!=item.Version || !versions.Add(item.Version) || !Path.IsPathFullyQualified(item.Directory) || item.Mode is not ("standalone" or "slim" or "legacy") || !ProductManifest.IsHash(item.ManifestHash)) throw new InvalidDataException("安装登记冲突或无效。");
        if(result.DefaultVersion!=null && !versions.Contains(result.DefaultVersion)) throw new InvalidDataException("默认版本未登记。");
        return result;
    }
    public IReadOnlyList<InstallationStatus> Inspect(bool verifyContent=true)=>Read().Installations.Select(i=>Inspect(i,verifyContent)).ToArray();
    public InstallationStatus Inspect(InstalledVersion item,bool verifyContent=true)
    {
        if(!Directory.Exists(item.Directory)) return new(item,"missing","安装目录不存在。");
        try
        {
            var manifest=ProductManifest.Read(ProductManifest.SafePath(item.Directory,"toolkit.json"));
            if(manifest.Digest!=item.ManifestHash || manifest.Version!=item.Version || manifest.Mode!=item.Mode) throw new InvalidDataException("安装身份或清单已改变。");
            if(verifyContent) manifest.VerifyDirectory(item.Directory);
            return new(item,"available",null);
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException) { return new(item,"corrupt",e.Message); }
    }
    public InstalledVersion Register(string directory,bool managed=false)
    {
        if(string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("请提供工具包目录。");
        directory=Path.GetFullPath(directory);
        if((Path.TrimEndingDirectorySeparator(Home)+Path.DirectorySeparatorChar).StartsWith(Path.TrimEndingDirectorySeparator(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("启动器设置不能位于产品目录内。");
        var manifest=ProductManifest.Read(ProductManifest.SafePath(directory,"toolkit.json")); manifest.VerifyDirectory(directory);
        using var gate=Lock(); var settings=Read();
        var existing=settings.Installations.FirstOrDefault(i=>i.Version==manifest.Version);
        if(existing!=null)
        {
            if(!existing.Directory.Equals(directory,StringComparison.OrdinalIgnoreCase)||existing.ManifestHash!=manifest.Digest) throw new InvalidOperationException("此版本已在另一位置或以不同内容登记。");
            return existing;
        }
        if(settings.Installations.Any(i=>Overlaps(i.Directory,directory))) throw new InvalidOperationException("版本目录不能互相包含。");
        var item=new InstalledVersion(manifest.Version,directory,manifest.Mode,manifest.Digest,managed);
        Write(settings with { Installations=[..settings.Installations,item], DefaultVersion=settings.DefaultVersion??item.Version }); return item;
    }
    public void SetDefault(string version)
    {
        version=ProductManifest.NormalizeVersion(version); using var gate=Lock(); var settings=Read();
        var item=settings.Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("所选版本未安装。");
        var status=Inspect(item); if(status.State!="available") throw new InvalidOperationException(status.Diagnostic);
        Write(settings with { DefaultVersion=version });
    }
    public void SetStorageRoot(string directory)
    {
        directory=Path.GetFullPath(directory); ProductManifest.SafePath(directory,"objects");
        using var gate=Lock(); var settings=Read();
        if(settings.Installations.Length!=0) throw new InvalidOperationException("已有安装时不能直接切换共享存储位置。");
        if(Overlaps(directory,AppContext.BaseDirectory) || directory.Equals(Home,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择独立于启动器程序和设置的存储子目录。");
        Directory.CreateDirectory(directory); Write(settings with { StorageRoot=directory });
    }
    internal FileStream Lock()
    {
        var path=ProductManifest.SafePath(Home,"registry.lock");
        for(var i=0;;i++) try { return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) when(i<100) { Thread.Sleep(50); }
    }
    internal void Write(LauncherSettings settings)
    {
        var temporary=ProductManifest.SafePath(Home,"settings-"+Guid.NewGuid().ToString("N")+".tmp");
        try { using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { JsonSerializer.Serialize(file,settings,Json); file.Flush(true); } File.Move(temporary,ProductManifest.SafePath(Home,"settings.json"),true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static bool Overlaps(string first,string second)
    {
        first=Path.TrimEndingDirectorySeparator(Path.GetFullPath(first))+Path.DirectorySeparatorChar; second=Path.TrimEndingDirectorySeparator(Path.GetFullPath(second))+Path.DirectorySeparatorChar;
        return first.StartsWith(second,StringComparison.OrdinalIgnoreCase)||second.StartsWith(first,StringComparison.OrdinalIgnoreCase);
    }
}
