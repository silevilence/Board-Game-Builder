using System.Reflection.PortableExecutable;
using BoardGame.Runtime;
using BoardGame.Kernel;

namespace BoardGame.AppRuntime;

public sealed record PlayerPackageSource(RuntimeStartup Startup,int Format,string Fingerprint,Action<string> Pack,
    Func<string,string> ReadFingerprint,string[] RequiredFiles,string Instructions,object Versions);

/// <summary>Verified precompiled host delivery; no compiler, content parser or board model.</summary>
public static class PrecompiledPlayer
{
    public static ReleaseManifest Preflight(string release,PlayerPackageSource content)
    {
        var manifest=ContentJson.Read<ReleaseManifest>(File.ReadAllBytes(Path.Combine(release,"release.json")));
        if(manifest.Api!=1 || manifest.Platform!="win-x64" || manifest.RuntimeApi!=1 || manifest.RuntimePlugins==null)
            throw new InvalidDataException("release.json: incompatible Player API/platform/runtime catalog");
        if(!manifest.GuiEntry || manifest.EditorDebug)throw new InvalidDataException("release.json: formal export requires a graphical Player without editor debug entry");
        if(manifest.PackageFormat!=content.Format)throw new InvalidDataException("release.json: unsupported package format "+content.Format);
        foreach(var plugin in content.Startup.Plugins)
            if(manifest.RuntimePlugins.GetValueOrDefault(plugin.Id)!=plugin.Version)throw new InvalidDataException("release.json: unavailable runtime plugin/version "+plugin.Id);
        ContentPath.Validate(manifest.EntryPoint);
        var stem=Path.GetFileNameWithoutExtension(manifest.EntryPoint);
        if(manifest.EntryPoint!=stem+".exe")throw new InvalidDataException("release.json: entry must be a root executable");
        foreach(var required in new[]{manifest.EntryPoint,stem+".dll",stem+".deps.json",stem+".runtimeconfig.json","coreclr.dll","hostfxr.dll","CordiSharp.dll"}.Concat(content.RequiredFiles))
            if(!manifest.Files.ContainsKey(required))throw new InvalidDataException("release.json: missing required runtime "+required);
        foreach(var file in manifest.Files)
        {
            ValidateName(file.Key);
            if(ContentJson.Hash(File.ReadAllBytes(SafeFile(release,file.Key)))!=file.Value)throw new InvalidDataException(file.Key+": release content hash mismatch");
        }
        foreach(var depsPath in manifest.Files.Keys.Where(p=>p.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase)))
        {
            using var deps=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(SafeFile(release,depsPath)));
            var target=deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
            var prefix=depsPath.Contains('/')?depsPath[..(depsPath.LastIndexOf('/')+1)]:"";
            foreach(var library in deps.RootElement.GetProperty("targets").GetProperty(target).EnumerateObject())
                foreach(var kind in new[]{"runtime","native"})
                    if(library.Value.TryGetProperty(kind,out var assets))foreach(var asset in assets.EnumerateObject())
                    {
                        if(asset.Name.EndsWith(".pdb",StringComparison.OrdinalIgnoreCase))continue;
                        var file=prefix+Path.GetFileName(asset.Name);
                        if(!manifest.Files.ContainsKey(file))throw new InvalidDataException("release.json: missing runtime dependency "+file);
                    }
        }
        using var executable=File.OpenRead(SafeFile(release,manifest.EntryPoint));using var pe=new PEReader(executable);
        if(pe.PEHeaders.PEHeader?.Subsystem!=Subsystem.WindowsGui)throw new InvalidDataException(manifest.EntryPoint+": console subsystem is forbidden in formal exports");
        return manifest;
    }
    public static void Export(string release,PlayerPackageSource content,string destination,Action<string>? additionalSteps=null,
        IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        var target=Path.GetFullPath(destination);
        if(Directory.Exists(target) || File.Exists(target))throw new IOException("Export target already exists; choose a new directory.");
        cancellation.ThrowIfCancellationRequested();progress?.Report("检查预编译 Player 与内容依赖…");
        var manifest=Preflight(release,content);var staging=target+".staging-"+Guid.NewGuid();Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("复制预编译 Player 与运行时…");
            foreach(var file in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();var output=Path.Combine(staging,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(SafeFile(release,file.Key),output);
                if(ContentJson.Hash(File.ReadAllBytes(output))!=file.Value)throw new InvalidDataException("Export copy hash mismatch: "+file.Key);
            }
            File.Copy(Path.Combine(release,"release.json"),Path.Combine(staging,"release.json"));
            progress?.Report("打包指定内容…");content.Pack(Path.Combine(staging,"game.bgm"));
            if(content.ReadFingerprint(Path.Combine(staging,"game.bgm"))!=content.Fingerprint)throw new InvalidDataException("Export package identity mismatch");
            if(manifest.EntryPoint!="Play.exe")File.Copy(Path.Combine(staging,manifest.EntryPoint),Path.Combine(staging,"Play.exe"));
            File.WriteAllText(Path.Combine(staging,"README.txt"),content.Instructions);
            var versions=System.Text.Json.Nodes.JsonNode.Parse(ContentJson.Write(content.Versions))!.AsObject();
            versions["api"]=manifest.Api;versions["player"]=manifest.Version;versions["kind"]="formal-player";
            File.WriteAllText(Path.Combine(staging,"versions.json"),versions.ToJsonString(ContentJson.Options));
            var protectedFiles=Directory.GetFiles(staging,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(staging,p),p=>ContentJson.Hash(File.ReadAllBytes(p)));
            additionalSteps?.Invoke(staging);ValidateTree(new DirectoryInfo(staging));
            foreach(var file in protectedFiles)
                if(ContentJson.Hash(File.ReadAllBytes(Path.Combine(staging,file.Key)))!=file.Value)throw new InvalidDataException("Export step changed Player runtime: "+file.Key);
            if(content.ReadFingerprint(Path.Combine(staging,"game.bgm"))!=content.Fingerprint)throw new InvalidDataException("Export step changed module package");
            cancellation.ThrowIfCancellationRequested();Directory.Move(staging,target);progress?.Report("导出完成："+target);
        }
        finally {if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private static string SafeFile(string directory,string relative)
    {
        ContentPath.Validate(relative);var root=Path.GetFullPath(directory);var current=root;
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked release directory: "+root);
        foreach(var part in relative.Split('/'))
        {current=Path.Combine(current,part);if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked release content: "+relative);}
        return current;
    }
    private static void ValidateName(string path)
    {
        ContentPath.Validate(path);
        if(path.Split('/').Any(p=>p.StartsWith("Editor",StringComparison.OrdinalIgnoreCase) || p.StartsWith("CordiSharp",StringComparison.OrdinalIgnoreCase) && p!="CordiSharp.dll") ||
            path.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".pdb",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Editor/development export content is forbidden: "+path);
    }
    private static void ValidateTree(DirectoryInfo directory)
    {
        if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked export content: "+directory.Name);
        foreach(var item in directory.EnumerateFileSystemInfos())
        {
            if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked export content: "+item.Name);
            ValidateName(item.Name);if(item is DirectoryInfo child)ValidateTree(child);
        }
    }
}
