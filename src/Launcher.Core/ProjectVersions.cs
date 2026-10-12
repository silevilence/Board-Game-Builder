using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record EngineBinding(int Api,string Version);
public sealed record VersionSelection(string Version,string Source,string? Project,string? BoundVersion,InstalledVersion? Installation,string State,string? Diagnostic)
{
    public InstalledVersion RequireAvailable()=>State=="available"&&Installation!=null?Installation:throw new InvalidOperationException(Diagnostic??"所选版本不可用。");
}

/// <summary>Author-only version choice. Game files and author plugin profiles are never rewritten.</summary>
public sealed class ProjectVersions(InstallationRegistry registry)
{
    public const string FileName="author.engine.json";
    public static string? Locate(string? project,string? currentDirectory=null)
    {
        var current=Path.GetFullPath(currentDirectory??Environment.CurrentDirectory);
        if(project!=null)
        {
            var path=Path.GetFullPath(project,current);
            if(Path.GetFileName(path).Equals("game.start.json",StringComparison.OrdinalIgnoreCase)) path=Path.GetDirectoryName(path)!;
            if(File.Exists(path)) throw new ArgumentException("工程位置必须为目录或 game.start.json。");
            return path;
        }
        for(var directory=new DirectoryInfo(current);directory!=null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"game.start.json"))||File.Exists(Path.Combine(directory.FullName,FileName))) return directory.FullName;
        return null;
    }
    public static EngineBinding? Read(string project)
    {
        var path=ProductManifest.SafePath(project,FileName);if(!File.Exists(path)) return null;
        if(new FileInfo(path).Length>4096) throw new InvalidDataException("工程引擎绑定文件过大。");
        var json=File.ReadAllText(path);using var document=JsonDocument.Parse(json);ProductManifest.Unique(document.RootElement);
        var binding=JsonSerializer.Deserialize<EngineBinding>(json,InstallationRegistry.Json);
        if(binding==null||binding.Api!=1||string.IsNullOrWhiteSpace(binding.Version)) throw new InvalidDataException("不支持的工程引擎绑定格式。");
        return binding with {Version=ProductManifest.NormalizeVersion(binding.Version)};
    }
    public VersionSelection Resolve(string? explicitVersion=null,string? project=null,string? currentDirectory=null)
    {
        project=Locate(project,currentDirectory);var binding=project==null?null:Read(project);var settings=registry.Read();
        var version=explicitVersion!=null?ProductManifest.NormalizeVersion(explicitVersion):binding?.Version??settings.DefaultVersion;
        var source=explicitVersion!=null?"explicit":binding!=null?"project":"default";
        if(version==null) throw new InvalidOperationException("工程尚未绑定且未选择默认版本，请显式选择引擎版本。");
        var item=settings.Installations.SingleOrDefault(i=>i.Version==version);
        if(item==null) return new(version,source,project,binding?.Version,null,"missing","V"+version+" 未安装，请先安装此版本；不会改用默认版本。");
        var status=registry.Inspect(item);
        return new(version,source,project,binding?.Version,item,status.State,status.State=="available"?null:"V"+version+" 需要修复或重新安装："+status.Diagnostic);
    }
    public EngineBinding? Bind(string project,string? version)
    {
        project=Locate(project)!;
        if(!Directory.Exists(project)) throw new DirectoryNotFoundException("请先创建工程目录，再保存作者版本绑定。");
        var path=ProductManifest.SafePath(project,FileName);
        // A separate explicit action is required even when launching with an override.
        using var gate=new FileStream(ProductManifest.SafePath(project,".author-engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
        _=Read(project);
        if(version==null) {if(File.Exists(path)) File.Delete(path);return null;}
        var binding=new EngineBinding(1,ProductManifest.NormalizeVersion(version));
        var temporary=ProductManifest.SafePath(project,".author-engine-"+Guid.NewGuid().ToString("N")+".tmp");
        try {using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,binding,InstallationRegistry.Json);file.Flush(true);} File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
        return binding;
    }
}
