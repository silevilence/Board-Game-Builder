# Windows 发行与导出

引擎维护者安装 .NET 10 SDK，运行：

```powershell
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/player-release
```

生成自包含 win-x64 Player 和独立自包含 Lua worker；运行时、图形库与框架 DLL 一并发布。
`release.json` 记录 API、发行版本、目标平台和每个文件的 SHA256。
发行目录不能原地覆盖；失败时使用新的空目录重试，避免混合两次构建。

作者只需要发行物，无需 SDK。导出命令本身是预编译 Player 的功能：

```powershell
./artifacts/player-release/Player.exe export artifacts/player-release samples/quick-cards artifacts/quick-cards-windows
```

导出先校验发行物与必需运行时，复制清单列出的文件、生成 game.bgm、Play.cmd 和玩法说明。
不创建 C# 工程，也不启动 SDK／编译器。导出目标必须不存在；失败清理独占的暂存目录，不覆盖已有产物。
双击 Play.cmd 或 Player.exe 进入图形流程。模块默认读取程序目录中的 game.bgm，不依赖当前工作目录。
CPU 工作进程优先运行 worker/Lua.exe，使用自己的运行时；开发构建使用相邻 Lua apphost。

发行 Player 不包含 Editor 项目或插件加载器；模块包按引用收集并剥离 editor_only 声明。
需要 Windows x64 / Direct3D 11；无声卡时音效自动静音，规则运行不受影响。

## 干净环境验证

```powershell
pwsh -NoProfile -File scripts/prepare-sandbox.ps1 -Export artifacts/quick-cards-windows
```

生成 `artifacts/sandbox-evidence/verify.wsb`：只读映射发行目录，单独映射证据目录可写，关闭网络。
打开该文件后，沙箱记录系统、dotnet 是否存在和 Player 两次完整对局／重新开局结果。
`games.json` 必须 success=true，environment.json 中无系统 dotnet；随后仍需核对双击入口与交互显示。
本机隐藏 DOTNET_ROOT/PATH 的测试只作为发行冒烟，不能代替真实干净 Windows 验收。
