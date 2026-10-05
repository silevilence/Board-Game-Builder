# Board Game Builder

M0 无图形界面运行时，开发环境为 .NET 10 SDK。

```powershell
dotnet build BoardGameBuilder.slnx
pwsh -NoProfile -File scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests
dotnet run --project src/Player -- --help
```

Kernel.Abstractions 定义数据与运行协议；Kernel 执行权威命令；Modules 负责目录加载；Lua 适配规则和决策；Runtime 供各宿主共享。View.Abstractions、View.MonoGame 与 Editor 在 M0 仅建立项目边界，窗口和渲染在 M1 实现。

运行卡牌示例与固定种子验收：

```powershell
dotnet run --project src/Player -- play samples/quick-cards --seed 42
dotnet run --project src/Player -- batch samples/quick-cards --seed 1 --count 20 --out artifacts/m0-validation
```

批量入口强制所有席位为 CPU；每个成功对局自动重跑并回放，核对完整状态和各席位投影。失败时退出码为 1，保留每局日志、诊断、种子和含原始脚本及初始局面的 module.snapshot.json。将该快照传给 batch 可重现原版本；replay 同时支持目录或快照。参数或加载错误退出码为 2。`--max-steps` 设置步数上限。
