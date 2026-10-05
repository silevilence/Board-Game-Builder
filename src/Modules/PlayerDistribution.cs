using BoardGame.Kernel;
namespace BoardGame.Modules;

public sealed record ReleaseManifest(int Api,string Version,string Platform,SortedDictionary<string,string> Files,bool GuiEntry=false,bool EditorDebug=false);
public static class PlayerDistribution
{
    public static ReleaseManifest Preflight(string release,string moduleDirectory)
    {
        var manifest=Wire.Read<ReleaseManifest>(File.ReadAllText(Path.Combine(release,"release.json")));
        if(manifest.Api!=EngineVersion.Api || manifest.Platform!="win-x64") throw new InvalidDataException("release.json: incompatible Player API/platform");
        if(!manifest.GuiEntry || manifest.EditorDebug) throw new InvalidDataException("release.json: formal export requires a graphical Player without editor debug entry");
        foreach(var required in new[] {"Player.exe","Player.runtimeconfig.json","coreclr.dll","hostfxr.dll","worker/Lua.exe","worker/coreclr.dll"})
            if(!manifest.Files.ContainsKey(required)) throw new InvalidDataException("release.json: missing required runtime "+required);
        foreach(var file in manifest.Files)
        {
            ModulePackage.ValidatePath(file.Key);
            if(file.Key.Split('/').Any(s=>s.StartsWith("Editor",StringComparison.OrdinalIgnoreCase)) || file.Key.EndsWith(".csproj") || file.Key.EndsWith(".pdb"))
                throw new InvalidDataException("release.json: editor/development content is forbidden: "+file.Key);
            var source=DirectoryModule.SafeFile(release,file.Key);
            if(ModulePackage.Hash(File.ReadAllBytes(source))!=file.Value) throw new InvalidDataException(file.Key+": release content hash mismatch");
        }
        using(var executable=File.OpenRead(Path.Combine(release,"Player.exe")))
        using(var pe=new System.Reflection.PortableExecutable.PEReader(executable))
            if(pe.PEHeaders.PEHeader?.Subsystem!=System.Reflection.PortableExecutable.Subsystem.WindowsGui) throw new InvalidDataException("Player.exe: console subsystem is forbidden in formal exports");
        _=DirectoryModule.Load(moduleDirectory);
        return manifest;
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
            ModulePackage.Pack(moduleDirectory,Path.Combine(staging,"game.bgm"));
            File.Copy(Path.Combine(staging,"Player.exe"),Path.Combine(staging,"Play.exe"));
            File.WriteAllText(Path.Combine(staging,"README.txt"),"Board Game Builder — Windows x64\r\n双击 Play.exe。选择真人席位和种子，点击开始；其余席位自动由 CPU 控制。\r\n右侧按钮执行规则动作；右键平移，滚轮或按钮缩放。\r\n使用结束对局停止故障对局，重新开局创建全新会话。\r\n无需编辑器、.NET SDK 或 .NET 运行时。需要 Direct3D 11 图形设备。\r\n");
            var module=ModulePackage.Load(Path.Combine(staging,"game.bgm"));
            File.WriteAllText(Path.Combine(staging,"versions.json"),Wire.Json(new {api=manifest.Api,player=manifest.Version,module=module.Id,module.Version,module.Fingerprint,rules=Wire.Hash(module.Rules),cpu=Wire.Hash(module.Cpu),kind="formal-player"}));
            progress?.Report("执行当前启用插件的导出附加步骤…"); additionalSteps?.Invoke(staging);
            foreach(var file in manifest.Files)
                if(ModulePackage.Hash(File.ReadAllBytes(DirectoryModule.SafeFile(staging,file.Key)))!=file.Value) throw new InvalidDataException("Export step changed Player runtime: "+file.Key);
            if(ModulePackage.Load(Path.Combine(staging,"game.bgm")).Fingerprint!=module.Fingerprint) throw new InvalidDataException("Export step changed module package");
            Directory.Move(staging,target);
            progress?.Report("导出完成："+target);
        }
        finally { if(Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
}
