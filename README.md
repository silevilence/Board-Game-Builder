# Board Game Builder

本地单人桌游运行时与 Windows 图形 Player。开发环境为 .NET 10 SDK；导出物自带运行时。

```powershell
dotnet build BoardGameBuilder.slnx
pwsh -NoProfile -File scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests
dotnet run --project src/Player -- --help
```

Kernel.Abstractions 定义数据与运行协议；Kernel 执行权威命令；Modules 负责目录／包加载与导出；Lua 适配规则和决策；Runtime 供各宿主共享。View.Abstractions 和 View.MonoGame 提供投影桌面与输入，Editor 当前为 Avalonia 最小视口验证壳。

```powershell
dotnet run --project src/Player -- gui samples/quick-cards
dotnet run --project src/Editor
dotnet run --project src/Player -- pack samples/quick-cards artifacts/quick-cards.bgm
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/player-release
./artifacts/player-release/Player.exe export artifacts/player-release samples/quick-cards artifacts/quick-cards-windows
```

双击导出目录中的 Play.cmd，配置真人席位和种子后开始；CPU 自动推进。
详见 [视口](docs/m1-hosting.md)、[模块包](docs/m1-packages.md)、[独立发行](docs/m1-distribution.md)。

运行卡牌示例与固定种子验收：

```powershell
dotnet run --project src/Player -- play samples/quick-cards --seed 42
dotnet run --project src/Player -- batch samples/quick-cards --seed 1 --count 20 --out artifacts/m0-validation
```

批量入口强制所有席位为 CPU；每个成功对局自动重跑并回放，核对完整状态和各席位投影。失败时退出码为 1，保留每局日志、诊断、种子和含原始脚本及初始局面的 module.snapshot.json。将该快照传给 batch 可重现原版本；replay 同时支持目录或快照。参数或加载错误退出码为 2。`--max-steps` 设置步数上限。

接口与边界见 [M0 运行时说明](docs/m0-runtime.md)，验证结果和审核修复见 [M0 验收记录](docs/validation/m0-review.md)。
