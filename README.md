# Board Game Builder

本地单人桌游运行时与 Windows 图形 Player。开发环境为 .NET 10 SDK；导出物自带运行时。

```powershell
dotnet build BoardGameBuilder.slnx -c Release
pwsh -NoProfile -File scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests -c Release
dotnet run --project src/Player -- --help
```

Kernel.Abstractions 定义数据与运行协议；Kernel 执行权威命令；Modules 提供定义解析与内容校验；Lua 适配规则和决策；Runtime 的独立 CordiSharp 宿主为 Player、试玩和批量验证装配加载器、内核、规则、CPU、扩展、席位及记录能力。View.Abstractions 和 View.MonoGame 提供投影桌面与输入，Editor.Core 管理作者文档，Avalonia Editor 使用独立插件上下文提供制作、两种试玩、调试和导出。

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
