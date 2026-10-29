# Windows 发行与导出

引擎维护者安装 .NET 10 SDK，运行：

```powershell
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/player-release
```

生成自包含 win-x64 Player 和独立自包含 Lua worker；运行时、图形库与框架 DLL 一并发布。
`release.json` 记录 API、发行版本、目标平台、每个文件的 SHA256，以及由已发布 Player 输出的实际运行插件目录。导出预检逐一匹配激活插件版本。
发行目录不能原地覆盖；失败时使用新的空目录重试，避免混合两次构建。

作者只需要发行物，无需 SDK。作者工具通过独立 `author.export.package`／`author.export.player` 提供 `project.pack`／`player.export`，Editor 从同一注册表发现方式及参数，草稿导出不隐式保存。`bgb.exe describe player.export` 显示当前契约；旧桌游 Player 兼容命令继续可用：

```powershell
./artifacts/player-release/Player.exe export artifacts/player-release samples/quick-cards artifacts/quick-cards-windows
```

导出先校验发行物与必需运行时，复制清单列出的文件、生成 game.bgm、Play.exe 和玩法说明。
不创建 C# 工程，也不启动 SDK／编译器。导出目标必须不存在；失败清理独占的暂存目录，不覆盖已有产物。
双击 Play.exe 或 Player.exe 进入图形流程。模块默认读取程序目录中的 game.bgm，不依赖当前工作目录，不经命令脚本启动。
CPU 工作进程优先运行 worker/Lua.exe，使用自己的运行时；开发构建使用相邻 Lua apphost。

发行 Player 包含游戏运行所需 CordiSharp.dll，排除 Editor 程序集、CordiSharp 编辑器专用扩展及开发文件。包只收集所选加载器、脚本和 WASM 配置引用的内容；运行入口不接受 editor_only 插件。
需要 Windows x64 / Direct3D 11；无声卡时音效自动静音，规则运行不受影响。

以上是完整桌游预编译组合。通用 `PrecompiledPlayer` 同样支持不含桌游实现的独立安装组合，校验 `entryPoint`、`packageFormat`、GUI 子系统、运行插件和完整依赖；内容由对应作者运行提供者打包。维护者发布非桌游最小 Player：

```powershell
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/minimal-player -HostProject samples/Minimal.Player/Minimal.Player.csproj -EntryPoint Minimal.Player.exe -PackageFormat 3 -IncludeLuaWorker:$false
```

`samples/motion-lab` 使用自己的作者插件，`player.export` 的 `player` 参数指向上述发行目录。它不包含 BoardGame.*、Kernel、Runtime、Modules、Lua 或 WASM；默认完整 Player 为兼容桌游而包含这些实现，两者不能混称最小发行。普通导出不涉及 NativeAOT、用户运行时编译或任意外部 .NET 插件。

## 干净环境验证

```powershell
pwsh -NoProfile -File scripts/prepare-sandbox.ps1 -Export artifacts/quick-cards-windows
```

生成 `artifacts/sandbox-evidence/verify.wsb`：只读映射发行目录，单独映射证据目录可写，关闭网络。
打开该文件后，沙箱记录系统、dotnet 是否存在和 Player 两次完整对局／重新开局结果。
`games.json` 必须 success=true，environment.json 中无系统 dotnet；随后仍需核对双击入口与交互显示。
本机隐藏 DOTNET_ROOT/PATH 的测试只作为发行冒烟，不能代替真实干净 Windows 验收。

小核心与非桌游联合验收使用 `scripts/validate-application-loop-windows.ps1`，在两个全新 Sandbox 中分别运行 Author／Player 阶段。第一轮映射预编译 toolkit、board、motion、minimal-player；第二轮只把第一轮 products 映射为 `C:\Input`，另映射可写 `C:\Evidence`，关闭网络。脚本检查无系统 dotnet、实际 Editor／CLI 导出、移动中文路径、真实图形输入、设备释放及可见控制台窗口。2026-10-29 两轮结果见 [干净 Windows 证据](validation/application-clean-windows.json)。当时经用户明确授权，仅验收 PowerShell 进程使用 RemoteSigned；宿主策略未修改。
