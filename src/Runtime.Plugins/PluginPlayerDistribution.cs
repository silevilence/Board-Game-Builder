using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

/// <summary>Optional external plugin delivery around the generic precompiled host exporter.</summary>
public static class PluginPlayerDistribution
{
    public const string HostFile="runtime-host.json";
    public const string DirectoryName="plugins/runtime";
    private static readonly JsonSerializerOptions json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static void WriteHostContract(string directory)=>File.WriteAllText(Path.Combine(directory,HostFile),JsonSerializer.Serialize(new HostContract(1,"win-x64",DirectoryName),json));
    public static string InstalledDirectory(string root)=>Path.Combine(Path.GetFullPath(root),DirectoryName);
    public static ReleaseManifest Preflight(string release,PlayerPackageSource content,IReadOnlyList<PluginBuildInput> imports)
    {
        if(imports.Count==0)return PrecompiledPlayer.Preflight(release,content);
        Validate(content,imports);
        var ids=imports.Select(p=>p.Manifest.Id).ToHashSet(StringComparer.Ordinal);
        var manifest=PrecompiledPlayer.Preflight(release,content with {Startup=content.Startup with {Plugins=content.Startup.Plugins.Where(p=>!ids.Contains(p.Id)).ToList()}});
        if(ids.Any(id=>manifest.RuntimePlugins!.ContainsKey(id)))throw new InvalidDataException("External implementation conflicts with a fixed Player plugin");
        if(!manifest.Files.ContainsKey(HostFile) || !manifest.Files.ContainsKey("Runtime.Plugins.dll"))throw new InvalidDataException("Player does not declare controlled external runtime plugin loading");
        var host=JsonSerializer.Deserialize<HostContract>(File.ReadAllText(Path.Combine(release,HostFile)),json);
        if(host is not {Format:1,Target:"win-x64",PluginDirectory:DirectoryName})throw new InvalidDataException("Player external runtime protocol is incompatible");
        if(manifest.Files.Keys.Any(p=>p.StartsWith(DirectoryName+"/",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Base Player already contains external plugin installations");
        return manifest;
    }
    public static void Export(string release,PlayerPackageSource content,IReadOnlyList<PluginBuildInput> imports,string destination,
        Action<string>? additionalSteps=null,IProgress<string>? progress=null,CancellationToken cancellation=default)
    {
        if(imports.Count==0){PrecompiledPlayer.Export(release,content,destination,additionalSteps,progress,cancellation);return;}
        cancellation.ThrowIfCancellationRequested();
        var target=Path.GetFullPath(destination);if(File.Exists(target) || Directory.Exists(target))throw new IOException("Export target already exists");
        var manifest=Preflight(release,content,imports);var stage=target+".plugins-"+Guid.NewGuid().ToString("N");
        try
        {
            var ids=imports.Select(p=>p.Manifest.Id).ToHashSet(StringComparer.Ordinal);
            PrecompiledPlayer.Export(release,content with {Startup=content.Startup with {Plugins=content.Startup.Plugins.Where(p=>!ids.Contains(p.Id)).ToList()}},stage,progress:progress,cancellation:cancellation);
            var files=new SortedDictionary<string,string>(manifest.Files,StringComparer.Ordinal);
            var plugins=new SortedDictionary<string,string>(manifest.RuntimePlugins!,StringComparer.Ordinal);
            foreach(var input in imports)
            {
                cancellation.ThrowIfCancellationRequested();var prefix=DirectoryName+"/"+Key(input.Manifest.Id,input.Manifest.Version)+"/";
                var folder=Path.Combine(stage,prefix);Directory.CreateDirectory(folder);
                var description=Path.Combine(folder,PluginManifest.FileName);File.WriteAllText(description,input.Manifest.Write());files.Add(prefix+PluginManifest.FileName,Hash(description));
                foreach(var file in input.Manifest.Files)
                {
                    cancellation.ThrowIfCancellationRequested();Verify(input,file);var output=Path.Combine(folder,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.Copy(Path.Combine(input.Directory,file.Path),output);var hash=Hash(output);
                    if(!hash.Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Changed plugin during export: "+file.Path);
                    files.Add(prefix+file.Path,hash);
                }
                plugins.Add(input.Manifest.Id,input.Manifest.Version);
            }
            File.WriteAllText(Path.Combine(stage,"release.json"),JsonSerializer.Serialize(manifest with {Files=files,RuntimePlugins=plugins},json));
            var protectedFiles=Directory.GetFiles(stage,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(stage,p),Hash);
            additionalSteps?.Invoke(stage);
            PrecompiledPlayer.ValidateTree(new DirectoryInfo(stage));
            foreach(var file in protectedFiles)if(Hash(Path.Combine(stage,file.Key))!=file.Value)throw new InvalidDataException("Export step changed runtime content: "+file.Key);
            _=PrecompiledPlayer.Preflight(stage,content);
            if(content.ReadFingerprint(Path.Combine(stage,"game.bgm"))!=content.Fingerprint)throw new InvalidDataException("External plugin game identity changed during export");
            cancellation.ThrowIfCancellationRequested();Directory.Move(stage,target);progress?.Report("外部运行插件与独立游戏已导出："+target);
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private static void Validate(PlayerPackageSource content,IReadOnlyList<PluginBuildInput> inputs)
    {
        if(inputs.Select(i=>i.Manifest.Id).Distinct(StringComparer.Ordinal).Count()!=inputs.Count)throw new InvalidDataException("Duplicate external publication identity");
        foreach(var input in inputs)
        {
            input.Manifest.Validate();var selection=content.Startup.Plugins.SingleOrDefault(p=>p.Id==input.Manifest.Id);
            if(selection==null || selection.Version!=input.Manifest.Version || selection.Api!=input.Manifest.Api || selection.Implementation!=input.Manifest.Fingerprint)
                throw new InvalidDataException(input.Manifest.Id+": external publication identity mismatch");
            if(!input.Manifest.Targets.Contains("win-x64",StringComparer.Ordinal))throw new InvalidDataException(input.Manifest.Id+": unsupported publication target");
            foreach(var file in input.Manifest.Files)Verify(input,file);
        }
    }
    private static void Verify(PluginBuildInput input,PluginFile file)
    {
        var path=Path.GetFullPath(Path.Combine(input.Directory,file.Path));
        for(var current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked plugin input: "+path);
        if(!Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(input.Manifest.Id+": plugin input changed: "+file.Path);
    }
    private static string Key(string id,string version)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id+"\0"+version)));
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private sealed record HostContract(int Format,string Target,string PluginDirectory);
}
