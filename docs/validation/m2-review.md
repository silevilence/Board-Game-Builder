# M2 单人闭环与整体审核

日期：2026-10-05。实施基线：`0c98699`。需求依据为 ROADMAP.md M2，历史设计未修改。
15 项按依赖顺序推进，逐项快速审核、原位勾选并本地提交；未开展 M3/M4。
本节先记录完整闭环验收，整体审核与修复将在所有任务提交后追加。

## 验收证据

可复查摘要：[m2-results.json](m2-results.json)。详细原始日志在以下本地目录。

| 验收 | 结果 | 原始材料 |
| --- | --- | --- |
| 卡牌 1..20 固定种子 | 全部终局、重跑和回放一致，每局 7 条命令 | artifacts/m2-cards-seeds |
| 六角 1..20 固定种子 | 全部终局、重跑和回放一致，每局 28..42 条命令 | artifacts/m2-hex-seeds |
| 两模式各 20 种子 | 两个示例的进程内与真实 Player 逐命令帧、完整版本日志、回放状态一致 | artifacts/m2-mode-gate |
| 实际 Avalonia 编辑器与 MonoGame 视口 | 两示例 YAML 等价、修改配置/Lua、撤销重做、保存重开、插件三次启停、两种完整试玩、非法动作、暂停单步和重放通过；作者初始局面不变 | artifacts/m2-workflow-cards、artifacts/m2-workflow-hex |
| 编辑器 GUI 操作导出 | 实际点击打开、保存、独立导出，显示完成并生成 Play.exe / versions.json | artifacts/m2-ui-export |
| 干净 Windows | 上述两示例编辑器导出物各完成两局图形对局与重开，退出码 0 | artifacts/m2-sandbox |

干净系统证据为真实 Windows Sandbox：Windows 10.0.26100.0，hasDotnet=false、sdkFolder=false、editorInstalled=false。
原 `.wsb` 自动启动未生成结果；通过微软 Windows Sandbox CLI 共享任务产物并在 ExistingLogin 会话运行验证后，生成 environment-final.json、Cards-games.json、Hex-games.json 及退出码文件。
这次没有把“移除 PATH”当作干净环境，也没有沿用 M1 的人工反馈替代 M2 验证。

正式 Play.exe 是 Windows GUI 子系统；没有 CMD 启动器，Lua worker 使用 CreateNoWindow。
构建去掉编辑器调试入口，实际调用 `--editor-debug --headless` 以退出码 2 拒绝。
必要加载/规则/设备错误显示在 GUI。依赖预检、目标目录冲突和插件附加步骤失败回滚测试通过。

完整测试入口已通过；包含脚本隔离/超限、CPU 错误、可见性、命令原子性、回放、包校验、插件依赖、作者文档和两模式调试。
工程边界检查通过。实际六角图形界面已检查棋盘、HP/AP、队友/对手与移动按钮。

## 复现

在仓库根目录运行，要求维护者安装 .NET 10 SDK：

```powershell
dotnet build BoardGameBuilder.slnx -c Release
./scripts/check-boundaries.ps1
dotnet run --project tests/Engine.Tests -c Release --no-build
dotnet run --project tests/Engine.Tests -c Release --no-build -- m2-gate
dotnet run --project src/Player -c Release --no-build -- batch samples/quick-cards --seed 1 --count 20 --out artifacts/recheck-cards
dotnet run --project src/Player -c Release --no-build -- batch samples/hex-skirmish --seed 1 --count 20 --out artifacts/recheck-hex
```

`scripts/validate-m2.ps1` 可在新输出目录构建发行物，并自动运行上述测试和两示例的真实编辑器工作流。
独立 Player 调试测试使用维护者构建，正式发行物禁用调试；两者使用同一 Player 项目、Kernel/Modules/Lua/View。
`Editor.exe --verify <模块目录> <新证据目录> <发行物目录>` 驱动实际窗口与视口，不用无图形替身。

干净系统另使用 `scripts/prepare-sandbox.ps1` 准备映射，或通过
[Windows Sandbox CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli)
将输出游戏目录只读共享、证据目录单独可写共享。执行 `Play.exe gui C:\Game\game.bgm --smoke C:\Evidence\games.json`。
`artifacts/m2-sandbox/verify-workflows.ps1` 是本轮实际运行脚本；用 PowerShell CLI 启动，没有通过 GUI 输入终端命令。

## 使用范围

操作说明见 [编辑器指南](../editor-guide.md)。仅验收 Windows x64，仍有原生 HWND airspace 限制。
Lua 规则/CPU 为受控脚本，编辑器插件是宿主信任的官方代码；第三方运行时 WASM 不在 M2。
YAML 编辑经 YamlDotNet 转换到相同组件配置，磁盘主文件仍为 module.json。
日志为作者调试材料，包含隐藏真值与脚本版本，普通玩家只见席位投影与脱敏消息。

## Standards

待所有 M2 任务提交后进行整体审核。

## Spec

待所有 M2 任务提交后进行整体审核。
