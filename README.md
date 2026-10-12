# Board Game Builder

本地单人桌游运行时与 Windows 图形 Player。开发环境为 .NET 10 SDK；导出物自带运行时。

Windows x64 工具包从 [GitHub Releases](https://github.com/silevilence/Board-Game-Builder/releases) 下载，包含 Editor、CLI、调试与正式 Player。独立包自带运行依赖；精简包使用兼容系统 Runtime，两者都不要求 SDK。下载校验、目录移动及维护者 tag 自动发布见 [发行说明](docs/releases.md)。

独立 [多版本启动器](docs/launcher.md) 支持历史 Release、共享安装、离线导入、工程版本绑定和固定版本 CLI／作者 SKILL。启动器不依赖预装引擎或系统 .NET；可运行 `scripts/publish-launcher.ps1 -Output <新目录>` 构建 Windows x64 自包含发行目录。两版真实发行和干净 Windows 的验证记录见 [启动器验收](docs/validation/launcher.md)。

作者可以使用 Editor、独立 CLI 或直接编辑游戏文件。Windows CLI 发行包不要求安装 SDK，包含插件、本地作者文档、技能模板和可供导出的 Player。开发运行：`dotnet run --project src/Cli -- help`；发行构建：`pwsh -NoProfile -File scripts/publish-author.ps1 -Output artifacts/author-release`。安装与工作区协作见 [CLI 作者工具](docs/author-cli.md)，离线创作入口见 [作者指南](docs/author/entry.md)。

```powershell
dotnet build BoardGameBuilder.slnx -c Release
pwsh -NoProfile -File scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests -c Release
dotnet run --project src/Player -- --help
```

Kernel.Abstractions 定义数据与运行协议；Kernel 执行权威命令；Modules 提供定义解析与内容校验；Lua 适配规则和决策；Runtime 的独立 CordiSharp 宿主为 Player、试玩和批量验证装配加载器、内核、规则、CPU、扩展、席位及记录能力。Authoring 提供项目视图、作者格式、编辑模式和领域操作，Cli 是其无界面入口。View.Abstractions 和 View.MonoGame 提供投影桌面与输入，Editor.Core 管理作者文档及 GUI 编辑模式，Avalonia Editor 使用独立插件上下文提供制作、两种试玩、调试和导出。

Networking 在 Runtime 外提供身份绑定、投影同步与恢复；无图形界面的 Server 和命令行 NetworkClient 通过 HTTP／HTTPS 协作，沿用相同的运行时、规则及 CPU 能力。Player 和编辑器的本地运行不依赖联机层。[联机说明](docs/networking.md) 包含启动、认证、断线恢复与记录访问约定；跨机人工验收仍待环境。

游戏目录以 `game.start.json` 为入口；它唯一声明包身份与运行插件配置。`definition.module` 加载器使用配置指定的组件定义文件；`definition.state` 支持独立状态格式。[运行插件与迁移协议](docs/runtime-plugins.md) 说明能力、版本及兼容策略；[最小无脚本示例](samples/state-minimal/game.start.json) 不启用 Lua 或 WASM。

M3.5 验收、整体审核及干净 Windows 检查见 [验证记录](docs/validation/m3.5-review.md)，全部示例入口见 [迁移清单](samples/README.md)。

旧目录须显式转换到新目录：`dotnet run --project src/Player -- migrate <旧目录> <新目录>`。旧 `.bgm` 须从原始目录迁移后重新打包，正常加载不会自动回退旧格式。

```powershell
dotnet run --project src/Player -- gui samples/quick-cards
dotnet run --project src/Editor
dotnet run --project src/Player -- pack samples/quick-cards artifacts/quick-cards.bgm
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/player-release
./artifacts/player-release/Player.exe export artifacts/player-release samples/quick-cards artifacts/quick-cards-windows
```

双击导出目录中的 Play.exe，按模块默认席位开始；开局设置可调整真人席位、种子及本地模式，CPU 自动推进。
详见 [视口](docs/m1-hosting.md)、[模块包](docs/m1-packages.md)、[独立发行](docs/m1-distribution.md)。

运行卡牌示例与固定种子验收：

```powershell
dotnet run --project src/Player -- play samples/quick-cards --seed 42
dotnet run --project src/Player -- batch samples/quick-cards --seed 1 --count 20 --out artifacts/m0-validation
```

批量入口强制所有席位为 CPU；每个成功对局自动重跑并回放，核对完整状态和各席位投影。失败时退出码为 1，保留每局日志、诊断、种子和含原始脚本及初始局面的 module.snapshot.json。将该快照传给 batch 可重现原版本；replay 同时支持目录或快照。参数或加载错误退出码为 2。`--max-steps` 设置步数上限。

接口与边界见 [M0 运行时说明](docs/m0-runtime.md)，验证结果和审核修复见 [M0 验收记录](docs/validation/m0-review.md)。

M2 操作见 [编辑器指南](docs/editor-guide.md) 与 [完整验收记录](docs/validation/m2-review.md)。
第二示例为 [六角小队战](samples/hex-skirmish/README.md)，支持真人与 CPU 队友对抗 CPU。

真实单人纸牌补充示例：[空当接龙](samples/freecell/README.md)，使用 Kenney 资源、Lua 规则和共享有序叠牌视图。种子 1 已验证可解，操作为「开始 → 洗牌 → 发牌」。

```powershell
dotnet run --project src/Player -- gui samples/freecell
dotnet run --project tests/Engine.Tests -c Release -- freecell freecell-shortcuts freecell-modes
```

本机独立产物：`artifacts/freecell-windows-1.2.0/Play.exe`。经典绿色牌桌与灰色菜单，自动适应窗口；支持点击选牌／目标、双击快速移牌、按住右键看牌及键盘快捷操作。完整键位见模块说明，证据与限制见 [空当接龙验收记录](docs/validation/freecell-review.md)。
