using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoardGame.Launcher;

public sealed record ProductFile(string Path, long Size, string Sha256, string Component, string Storage);
public sealed record ComponentSource(string Repository, string Tag, string Asset);
public sealed record ProductComponent(string Id, string Kind, string Identity, string[] Objects, long Size, string Sha256, ComponentSource Source);

/// <summary>A fully resolved product description, independent of any installed engine.</summary>
public sealed class ProductManifest
{
    public static readonly IReadOnlyDictionary<string,string> RequiredEntries = new Dictionary<string,string>
    { ["editor"]="editor/Editor.exe", ["cli"]="bgb.exe", ["player"]="player/Player.exe", ["debugPlayer"]="debug-player/Player.exe", ["documentation"]="docs/author/entry.md" };
    public string Version { get; }
    public string Mode { get; }
    public bool Legacy { get; }
    public JsonElement Json { get; }
    public IReadOnlyList<ProductFile> Files { get; }
    public IReadOnlyList<ProductComponent> Components { get; }
    public byte[] Bytes { get; }
    public string Digest => Hash(Bytes);

    private ProductManifest(byte[] bytes)
    {
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("版本清单过大。");
        Bytes = bytes.ToArray();
        using var document = JsonDocument.Parse(bytes.AsMemory(bytes.AsSpan().StartsWith(new byte[]{239,187,191}) ? 3 : 0));
        Json = document.RootElement.Clone(); Unique(Json);
        Version = NormalizeVersion(Text(Json,"version"));
        if (Text(Json,"version") != Version || Text(Json,"platform") != "win-x64") Fail("不支持的产品版本或平台");
        var entries = Json.GetProperty("entries");
        if (entries.EnumerateObject().Count()!=RequiredEntries.Count || RequiredEntries.Any(e=>Text(entries,e.Key)!=e.Value)) Fail("缺少必需入口");
        var api=Json.GetProperty("api").GetInt32();
        Legacy=api==1;
        if (Legacy)
        {
            if (Version!="0.1.0" || Json.TryGetProperty("format",out _) || Json.TryGetProperty("components",out _)) Fail("未知旧包格式");
            Mode="legacy"; Files=[]; Components=[]; return;
        }
        if (api!=2 || Text(Json,"format")!="board-game-builder.toolkit" || Text(Json,"architecture")!="x64" || Text(Json,"layout")!="hardlinks-v1") Fail("未知版本清单或存储格式");
        Mode=Text(Json,"packageMode"); if(Mode is not ("standalone" or "slim")) Fail("未知运行时模式");
        var files=new Dictionary<string,ProductFile>(StringComparer.Ordinal);
        var folded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sizes=new Dictionary<string,long>();
        foreach(var f in Json.GetProperty("files").EnumerateArray())
        {
            var path=Relative(Text(f,"path")); var hash=Text(f,"sha256"); var size=f.GetProperty("size").GetInt64();
            if(path.Equals("toolkit.json",StringComparison.OrdinalIgnoreCase) || path.Split('/')[0].Equals(".state",StringComparison.OrdinalIgnoreCase) || !folded.Add(path) || !IsHash(hash) || size is <0 or >2147483648) Fail("非法文件身份或路径冲突");
            if(sizes.TryGetValue(hash,out var old) && old!=size) Fail("同一内容大小冲突"); sizes[hash]=size;
            files.Add(path,new(path,size,hash,Text(f,"component"),Relative(Text(f,"storage"))));
        }
        if(files.Count is 0 or >20000) Fail("文件数超出限制");
        foreach(var path in folded) for(var parent=Path.GetDirectoryName(path)?.Replace('\\','/'); !string.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent)?.Replace('\\','/')) if(folded.Contains(parent)) Fail("文件与目录冲突");
        var storage=new Dictionary<string,string>();
        foreach(var f in files.Values)
        {
            if(!files.TryGetValue(f.Storage,out var canonical) || canonical.Storage!=canonical.Path || canonical.Sha256!=f.Sha256 || canonical.Size!=f.Size) Fail("无效内容存储路径");
            if(storage.TryGetValue(f.Sha256,out var previous) && previous!=f.Storage) Fail("重复内容存储"); storage[f.Sha256]=f.Storage;
        }
        Components=ReadComponents(Json.GetProperty("components"));
        var owners=new Dictionary<string,string>();
        foreach(var c in Components) foreach(var h in c.Objects) if(!sizes.ContainsKey(h) || !owners.TryAdd(h,c.Id)) Fail("组件对象缺失或重复");
        if(files.Values.Any(f=>!owners.TryGetValue(f.Sha256,out var id)||id!=f.Component)) Fail("文件组件未解析");
        var targets=Json.GetProperty("entryTargets");
        if(targets.EnumerateObject().Count()!=4) Fail("无效入口映射");
        foreach(var e in RequiredEntries)
        {
            if(!files.ContainsKey(e.Value)) Fail("入口未列入清单");
            if(e.Key!="documentation" && (Text(targets,e.Value)!=".payload/"+e.Value || !files.ContainsKey(".payload/"+e.Value))) Fail("无效原生入口目标");
        }
        var libraries=new HashSet<string>();
        foreach(var library in Json.GetProperty("libraries").EnumerateArray())
            if(Text(library,"id")!=Text(library,"name")+"/"+Text(library,"version") || !libraries.Add(Text(library,"id"))) Fail("无效库身份");
        foreach(var f in Json.GetProperty("files").EnumerateArray()) if(Strings(f,"libraries").Any(l=>!libraries.Contains(l))) Fail("文件引用未知库");
        var programs=new HashSet<string>();
        foreach(var p in Json.GetProperty("programs").EnumerateArray())
        {
            var entry=Text(p,"entry"); var used=Strings(p,"files"); var libs=Strings(p,"libraries");
            if(!programs.Add(entry) || used.Any(f=>!files.ContainsKey(f)) || libs.Any(l=>!libraries.Contains(l)) || new[]{entry,Text(p,"runtimeConfig"),Text(p,"dependencies")}.Any(f=>!used.Contains(f))) Fail("程序依赖未解析");
            var frameworks=new HashSet<string>();
            foreach(var r in p.GetProperty("frameworks").EnumerateArray())
            {
                var name=Text(r,"framework"); var version=NormalizeVersion(Text(r,"privateVersion"));
                if(name is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App") || !frameworks.Add(name) || Text(r,"minimumVersion")!=version || Text(r,"rollForward")!="LatestPatch") Fail("不支持的运行时策略");
                if(Mode=="standalone" && !libs.Contains("runtimepack."+name+".Runtime.win-x64/"+version)) Fail("私有运行时身份不一致");
            }
            if(!frameworks.Contains("Microsoft.NETCore.App")) Fail("缺少核心运行时要求");
        }
        if(RequiredEntries.Where(e=>e.Key!="documentation").Any(e=>!programs.Contains(".payload/"+e.Value))) Fail("缺少程序依赖关系");
        if(Mode=="slim")
        {
            var export=Json.GetProperty("exportDependencies");
            if(export.GetProperty("api").GetInt32()!=1 || Text(export,"format")!="board-game-builder.player-components" || Text(export,"version")!=Version || Text(export,"platform")!="win-x64") Fail("未知导出依赖描述");
            var exports=ReadComponents(export.GetProperty("components")); var exportOwners=new Dictionary<string,string>();
            foreach(var c in exports) foreach(var h in c.Objects) if(!exportOwners.TryAdd(h,c.Id)) Fail("重复导出对象");
            var exportPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var exportSizes=new Dictionary<string,long>();
            foreach(var f in export.GetProperty("files").EnumerateArray())
            {
                var size=f.GetProperty("size").GetInt64(); var hash=Text(f,"sha256");
                if(!exportPaths.Add(Relative(Text(f,"path"))) || !exportOwners.TryGetValue(hash,out var id) || id!=Text(f,"component") || size is <0 or >2147483648 || (exportSizes.TryGetValue(hash,out var previous)&&previous!=size)) Fail("无效导出依赖");
                exportSizes[hash]=size;
            }
            foreach(var path in exportPaths) for(var parent=Path.GetDirectoryName(path)?.Replace('\\','/'); !string.IsNullOrEmpty(parent); parent=Path.GetDirectoryName(parent)?.Replace('\\','/')) if(exportPaths.Contains(parent)) Fail("导出文件与目录冲突");
            if(!exportPaths.Contains("release.json") || !exportPaths.Contains("Player.exe")) Fail("缺少正式导出入口");
        }
        Files=files.Values.ToArray();
    }

    public static ProductManifest Parse(byte[] bytes)
    {
        try { return new(bytes); }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException) { throw new InvalidDataException("版本清单无效："+e.Message,e); }
    }
    public static ProductManifest Read(string path)
    {
        if(new FileInfo(path).Length>16*1024*1024) Fail("版本清单过大");
        return Parse(File.ReadAllBytes(path));
    }
    public static string NormalizeVersion(string value)
    {
        var plain=value.TrimStart('V','v');
        if(!Regex.IsMatch(plain,@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") || !System.Version.TryParse(plain,out _)) Fail("版本必须为 V主.次.修订");
        if(value.Length-plain.Length>1) Fail("无效版本前缀"); return plain;
    }
    internal static ProductComponent[] ReadComponents(JsonElement array)
    {
        var result=new List<ProductComponent>(); var ids=new HashSet<string>();
        foreach(var c in array.EnumerateArray())
        {
            var kind=Text(c,"kind"); var identity=Text(c,"identity"); var objects=Strings(c,"objects");
            var canonical=JsonSerializer.Serialize(new { group=identity, objects=objects.Order(StringComparer.Ordinal) },new JsonSerializerOptions { WriteIndented=true, NewLine="\n", Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })+"\n";
            var hash=Hash(Encoding.UTF8.GetBytes(canonical)); var id=kind+"-"+hash; var source=c.GetProperty("source");
            if(kind is not ("runtime" or "dependency" or "product") || !identity.StartsWith(kind,StringComparison.Ordinal) || id!=Text(c,"id") || !ids.Add(id) || objects.Length is 0 or >20000 || objects.Distinct().Count()!=objects.Length || objects.Any(h=>!IsHash(h)) || !IsHash(Text(c,"sha256")) || c.GetProperty("size").GetInt64() is <=0 or >=2147483648 || !Regex.IsMatch(Text(source,"repository"),@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z") || Text(source,"tag")!="components-"+hash || Text(source,"asset")!=id+".zip") Fail("无效固定组件身份或来源");
            result.Add(new(id,kind,identity,objects,c.GetProperty("size").GetInt64(),Text(c,"sha256"),new(Text(source,"repository"),Text(source,"tag"),Text(source,"asset"))));
        }
        if(result.Count is 0 or >1000) Fail("组件数超出限制"); return result.ToArray();
    }
    public void VerifyDirectory(string root)
    {
        SafePath(root,"toolkit.json");
        if(Legacy) { foreach(var e in RequiredEntries.Values) if(!File.Exists(SafePath(root,e))) Fail("旧包入口缺失："+e); return; }
        foreach(var f in Files.DistinctBy(f=>f.Storage)) VerifyFile(SafePath(root,f.Storage),f.Size,f.Sha256);
        foreach(var f in Files.Where(f=>f.Path!=f.Storage)) if(File.Exists(SafePath(root,f.Path))) VerifyFile(SafePath(root,f.Path),f.Size,f.Sha256);
    }
    public static string Relative(string path)
    {
        if(string.IsNullOrEmpty(path) || path.Contains('\\') || path.Split('/').Any(p=>p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.Any(c=>c<32||"<>:\"|?*".Contains(c)) || Regex.IsMatch(p,@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z",RegexOptions.IgnoreCase))) Fail("非法相对路径："+path);
        return path;
    }
    public static string SafePath(string root,string relative)
    {
        var path=Path.GetFullPath(Path.Combine(Path.GetFullPath(root),Relative(relative)));
        for(var item=new FileInfo(path) as FileSystemInfo; item!=null; item=Directory.GetParent(item.FullName))
            if((item.Exists || File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName)&FileAttributes.ReparsePoint)!=0) Fail("不允许目录或文件链接："+item.FullName);
        return path;
    }
    public static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static bool IsHash(string value)=>value.Length==64&&value.All(char.IsAsciiHexDigitLower);
    public static string HashFile(string path) { using var file=File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    public static void VerifyFile(string path,long size,string hash) { if(!File.Exists(path)||new FileInfo(path).Length!=size||HashFile(path)!=hash) Fail("文件缺失或损坏："+path); }
    internal static string Text(JsonElement element,string key)=>element.GetProperty(key).GetString()??throw new InvalidDataException("缺少字段："+key);
    internal static string[] Strings(JsonElement element,string key)=>element.GetProperty(key).EnumerateArray().Select(e=>e.GetString()??throw new InvalidDataException("空数组项")).ToArray();
    internal static void Unique(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object) { var keys=new HashSet<string>(); foreach(var p in element.EnumerateObject()) { if(!keys.Add(p.Name)) Fail("重复 JSON 字段"); Unique(p.Value); } }
        else if(element.ValueKind==JsonValueKind.Array) foreach(var item in element.EnumerateArray()) Unique(item);
    }
    private static void Fail(string message)=>throw new InvalidDataException(message+"。");
}
