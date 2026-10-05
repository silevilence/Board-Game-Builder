# Board Game Builder

M0 无图形界面运行时，开发环境为 .NET 10 SDK。

```powershell
dotnet build BoardGameBuilder.slnx
pwsh -NoProfile -File scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests
dotnet run --project src/Player -- --help
```

Kernel.Abstractions 定义数据与运行协议；Kernel 执行权威命令；Modules 负责目录加载；Lua 适配规则和决策；Runtime 供各宿主共享。View.Abstractions、View.MonoGame 与 Editor 在 M0 仅建立项目边界，窗口和渲染在 M1 实现。
