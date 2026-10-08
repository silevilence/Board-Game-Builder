using BoardGame.Kernel;
using BoardGame.Modules;
namespace BoardGame.Runtime;

public sealed record ReleaseManifest(int Api,string Version,string Platform,SortedDictionary<string,string> Files,bool GuiEntry=false,bool EditorDebug=false,
    SortedDictionary<string,string>? RuntimePlugins=null,int RuntimeApi=1);
public static class PlayerDistribution
{
    public static ReleaseManifest Preflight(string release,string moduleDirectory)
    {
        var manifest=Wire.Read<ReleaseManifest>(File.ReadAllText(Path.Combine(release,"release.json")));
        if(manifest.Api!=EngineVersion.Api || manifest.Platform!="win-x64") throw new InvalidDataException("release.json: incompatible Player API/platform");
        if(!manifest.GuiEntry || manifest.EditorDebug) throw new InvalidDataException("release.json: formal export requires a graphical Player without editor debug entry");
        var module=GameRuntime.Load(moduleDirectory);
        if(manifest.RuntimeApi!=1 || manifest.RuntimePlugins==null) throw new InvalidDataException("release.json: missing/incompatible runtime plugin catalog");
        foreach(var plugin in module.RuntimeIdentity!.Startup.Plugins)
            if(manifest.RuntimePlugins.GetValueOrDefault(plugin.Id)!=plugin.Version) throw new InvalidDataException("release.json: unavailable runtime plugin/version "+plugin.Id);
        foreach(var required in new[] {"Player.exe","Player.dll","Player.deps.json","Player.runtimeconfig.json","Runtime.dll","Modules.dll","Kernel.dll","Kernel.Abstractions.dll","coreclr.dll","hostfxr.dll","CordiSharp.dll"}
            .Concat(RuntimeContent.Scripts(module.RuntimeIdentity.Startup).Any()?new[]{"worker/Lua.exe","worker/coreclr.dll"}:[]))
            if(!manifest.Files.ContainsKey(required)) throw new InvalidDataException("release.json: missing required runtime "+required);
        foreach(var file in manifest.Files)
        {
            ModulePackage.ValidatePath(file.Key);
            if(file.Key.Split('/').Any(Forbidden) || file.Key.EndsWith(".csproj") || file.Key.EndsWith(".pdb"))
                throw new InvalidDataException("release.json: editor/development content is forbidden: "+file.Key);
            var source=DirectoryModule.SafeFile(release,file.Key);
            if(ModulePackage.Hash(File.ReadAllBytes(source))!=file.Value) throw new InvalidDataException(file.Key+": release content hash mismatch");
        }
        ValidateDependencies("Player.deps.json","");
        if(RuntimeContent.Scripts(module.RuntimeIdentity.Startup).Any()) ValidateDependencies("worker/Lua.deps.json","worker/");
        using(var executable=File.OpenRead(Path.Combine(release,"Player.exe")))
        using(var pe=new System.Reflection.PortableExecutable.PEReader(executable))
            if(pe.PEHeaders.PEHeader?.Subsystem!=System.Reflection.PortableExecutable.Subsystem.WindowsGui) throw new InvalidDataException("Player.exe: console subsystem is forbidden in formal exports");
        if(module.RuntimePlugins is {Count:>0} && (!manifest.Files.ContainsKey("Wasmtime.Dotnet.dll") || !manifest.Files.ContainsKey("wasmtime.dll")))
            throw new InvalidDataException("release.json: Player release is missing the WASM runtime; use an updated official Player distribution");
        return manifest;
        void ValidateDependencies(string depsPath,string prefix)
        {
            if(!manifest.Files.ContainsKey(depsPath)) throw new InvalidDataException("release.json: missing required runtime "+depsPath);
            using var deps=System.Text.Json.JsonDocument.Parse(File.ReadAllText(DirectoryModule.SafeFile(release,depsPath)));
            var target=deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
            foreach(var library in deps.RootElement.GetProperty("targets").GetProperty(target).EnumerateObject())
                foreach(var kind in new[]{"runtime","native"})
                    if(library.Value.TryGetProperty(kind,out var assets)) foreach(var asset in assets.EnumerateObject())
                    {
                        if(asset.Name.EndsWith(".pdb",StringComparison.OrdinalIgnoreCase)) continue;
                        var file=prefix+Path.GetFileName(asset.Name);
                        if(!manifest.Files.ContainsKey(file)) throw new InvalidDataException("release.json: missing runtime dependency "+file);
                    }
        }
    }
    public static void Export(string release,string moduleDirectory,string destination,Action<string>? additionalSteps=null,IProgress<string>? progress=null)
    {
        progress?.Report("检查 Player、模块与依赖…");
        var manifest=Preflight(release,moduleDirectory);
        var target=Path.GetFullPath(destination);
        if(Directory.Exists(target) || File.Exists(target)) throw new IOException("Export target already exists; choose a new directory.");
        var staging=target+".staging-"+Guid.NewGuid(); Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("复制预编译 Player 与运行时…");
            foreach(var file in manifest.Files)
            {
                var output=Path.Combine(staging,file.Key); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(DirectoryModule.SafeFile(release,file.Key),output);
            }
            File.Copy(Path.Combine(release,"release.json"),Path.Combine(staging,"release.json"));
            progress?.Report("打包模块…");
            GamePackage.Pack(moduleDirectory,Path.Combine(staging,"game.bgm"));
            File.Copy(Path.Combine(staging,"Player.exe"),Path.Combine(staging,"Play.exe"));
            File.WriteAllText(Path.Combine(staging,"README.txt"),"Board Game Builder — Windows x64\r\n双击 Play.exe，按模块默认席位直接进入游戏。\r\n游戏 → 开局设置（F3）可选择种子、真人／CPU 和本地模式；确认后开始新对局，取消保留当前对局。\r\n游戏菜单可新开、重开或结束对局；文件菜单可加载模块或显示工具栏。\r\n查看菜单可适应窗口、显示／隐藏动作栏；棋盘支持中键平移、滚轮缩放。\r\n热座交接时由下一位玩家确认接手；同屏模式可被旁观。\r\n需工具界面时运行 Play.exe gui --shell tools。\r\n无需编辑器、.NET SDK 或 .NET 运行时。需要 Direct3D 11 图形设备。\r\n");
            var module=GameRuntime.Load(Path.Combine(staging,"game.bgm"));
            File.WriteAllText(Path.Combine(staging,"versions.json"),Wire.Json(new {api=manifest.Api,player=manifest.Version,module=module.Id,module.Version,module.Fingerprint,rules=Wire.Hash(module.Rules),cpu=Wire.Hash(module.Cpu),kind="formal-player"}));
            var protectedFiles=Directory.GetFiles(staging,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(staging,p).Replace('\\','/'),p=>ModulePackage.Hash(File.ReadAllBytes(p)));
            progress?.Report("执行当前启用插件的导出附加步骤…"); additionalSteps?.Invoke(staging);
            ValidateExtras(new DirectoryInfo(staging));
            foreach(var file in protectedFiles)
                if(ModulePackage.Hash(File.ReadAllBytes(DirectoryModule.SafeFile(staging,file.Key)))!=file.Value) throw new InvalidDataException("Export step changed Player runtime: "+file.Key);
            if(GameRuntime.Load(Path.Combine(staging,"game.bgm")).Fingerprint!=module.Fingerprint) throw new InvalidDataException("Export step changed module package");
            Directory.Move(staging,target);
            progress?.Report("导出完成："+target);
        }
        finally { if(Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
    private static void ValidateExtras(DirectoryInfo directory)
    {
        foreach(var item in directory.EnumerateFileSystemInfos())
        {
            if((item.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Linked export content is forbidden: "+item.Name);
            if(Forbidden(item.Name) || item.Extension.Equals(".csproj",StringComparison.OrdinalIgnoreCase) || item.Extension.Equals(".pdb",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Editor/development export content is forbidden: "+item.Name);
            if(item is DirectoryInfo child) ValidateExtras(child);
        }
    }
    private static bool Forbidden(string name)=>name.StartsWith("Editor",StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CordiSharp",StringComparison.OrdinalIgnoreCase) && !name.Equals("CordiSharp.dll",StringComparison.OrdinalIgnoreCase);
}
