using BoardGame.Kernel;
namespace BoardGame.Modules;

public sealed record ReleaseManifest(int Api,string Version,string Platform,SortedDictionary<string,string> Files);
public static class PlayerDistribution
{
    public static void Export(string release,string moduleDirectory,string destination)
    {
        var manifest=Wire.Read<ReleaseManifest>(File.ReadAllText(Path.Combine(release,"release.json")));
        if(manifest.Api!=EngineVersion.Api || manifest.Platform!="win-x64") throw new InvalidDataException("release.json: incompatible Player API/platform");
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
        var target=Path.GetFullPath(destination);
        if(Directory.Exists(target) || File.Exists(target)) throw new IOException("Export target already exists; choose a new directory.");
        var staging=target+".staging-"+Guid.NewGuid(); Directory.CreateDirectory(staging);
        try
        {
            foreach(var file in manifest.Files)
            {
                var output=Path.Combine(staging,file.Key); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(DirectoryModule.SafeFile(release,file.Key),output);
            }
            File.Copy(Path.Combine(release,"release.json"),Path.Combine(staging,"release.json"));
            ModulePackage.Pack(moduleDirectory,Path.Combine(staging,"game.bgm"));
            File.WriteAllText(Path.Combine(staging,"Play.cmd"),"@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"%~dp0Player.exe\" gui \"%~dp0game.bgm\"\r\n");
            File.WriteAllText(Path.Combine(staging,"README.txt"),"Board Game Builder — Windows x64\r\n双击 Play.cmd。选择真人席位和种子，点击开始；其余席位自动由 CPU 控制。\r\nquick-cards：洗牌发牌后，每回合打出一张手牌，三轮后总点数较高者获胜。\r\n右侧按钮执行规则动作，也可把手牌拖到 table。右键拖动相机，滚轮缩放。\r\n使用结束对局停止故障对局，开始/重新开局创建全新会话。\r\n不需要安装编辑器、.NET SDK 或 .NET 运行时。需要 Windows Direct3D 11 图形设备。\r\n");
            Directory.Move(staging,target);
        }
        finally { if(Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
}
