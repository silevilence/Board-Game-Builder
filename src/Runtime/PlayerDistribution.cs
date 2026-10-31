using BoardGame.AppRuntime;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Board content adapter for the format-neutral precompiled host delivery service.</summary>
public static class PlayerDistribution
{
    public static ReleaseManifest Preflight(string release,string moduleDirectory)=>
        PrecompiledPlayer.Preflight(ExportDependencies.Resolve(release,new(AllowDownload:false)),Source(moduleDirectory));
    public static void Export(string release,string moduleDirectory,string destination,Action<string>? additionalSteps=null,IProgress<string>? progress=null,
        PlayerDependencyOptions? dependencies=null,CancellationToken cancellation=default)=>
        PrecompiledPlayer.Export(ExportDependencies.Resolve(release,dependencies,progress,cancellation),Source(moduleDirectory),destination,additionalSteps,progress,cancellation);
    private static PlayerPackageSource Source(string directory)
    {
        var module=GameRuntime.Load(directory);
        _=BoardRenderContract.Require(module.RuntimeIdentity!.Startup);
        string[] required=["Runtime.dll","Modules.dll","Kernel.dll","Kernel.Abstractions.dll",
            ..RuntimeContent.NeedsLua(module)?new[]{"worker/Lua.exe","worker/coreclr.dll"}:[],
            ..module.RuntimePlugins is {Count:>0}?new[]{"Wasmtime.Dotnet.dll","wasmtime.dll"}:[],..BoardGame.Rendering.GraphicsBackendContract.DistributionFiles(module.RuntimeIdentity.Startup)];
        return new(module.RuntimeIdentity!.Startup,2,module.Fingerprint,p=>GamePackage.Pack(directory,p),p=>GameRuntime.Load(p).Fingerprint,required,
            "Board Game Builder — Windows x64\r\n双击 Play.exe，按模块默认席位直接进入游戏。\r\n游戏 → 开局设置（F3）可选择种子、真人／CPU 和本地模式；确认后开始新对局，取消保留当前对局。\r\n游戏菜单可新开、重开或结束对局；文件菜单可加载模块或显示工具栏。\r\n查看菜单可适应窗口、显示／隐藏动作栏；棋盘支持中键平移、滚轮缩放。\r\n热座交接时由下一位玩家确认接手；同屏模式可被旁观。\r\n需工具界面时运行 Play.exe gui --shell tools。\r\n无需编辑器、.NET SDK 或 .NET 运行时。"+BoardGame.Rendering.GraphicsBackendContract.PlatformRequirements(module.RuntimeIdentity.Startup)+"\r\n",
            System.Text.Json.JsonSerializer.SerializeToElement(new{module=module.Id,module.Version,module.Fingerprint,rules=Wire.Hash(module.Rules),cpu=Wire.Hash(module.Cpu)},Wire.Options));
    }
}
