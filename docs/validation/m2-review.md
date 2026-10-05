# M2 单人闭环与整体审核

日期：2026-10-05。实施基线：`0c98699`。需求依据为 ROADMAP.md M2，历史设计未修改。
15 项按依赖顺序推进，逐项快速审核、原位勾选并本地提交；未开展 M3/M4。
全部任务提交后，以 `0c98699..07faebe` 为固定范围分别完成 Standards 与 Spec 整体审核，并集中修复下述问题。两条审核线对修复差异再次复核，均未发现遗留阻塞项。

## 验收证据

可复查摘要：[m2-results.json](m2-results.json)。详细原始日志在以下本地目录。

| 验收 | 结果 | 原始材料 |
| --- | --- | --- |
| 卡牌 1..20 固定种子 | 全部终局、重跑和回放一致，每局 7 条命令 | artifacts/m2-final/quick-cards-seeds |
| 六角 1..20 固定种子 | 全部终局、重跑和回放一致，每局 28..42 条命令 | artifacts/m2-final/hex-skirmish-seeds |
| 两模式各 20 种子 | 两个示例的进程内与真实 Player 逐命令帧、完整版本日志、回放状态一致 | artifacts/m2-mode-gate |
| 实际 Avalonia 编辑器与 MonoGame 视口 | 两示例 YAML 等价、修改配置/Lua、撤销重做、保存重开、插件三次启停、两种完整试玩、非法动作、暂停单步和重放通过；作者初始局面不变；草稿／席位／忙时暂停回归通过 | artifacts/m2-final/quick-cards-workflow、artifacts/m2-final/hex-skirmish-workflow |
| 编辑器 GUI 操作导出 | 实际点击打开、保存、独立导出，显示完成并生成 Play.exe / versions.json | artifacts/m2-ui-export |
| 干净 Windows | 上述两示例编辑器导出物各完成两局图形对局与重开，退出码 0 | artifacts/m2-sandbox |

干净系统证据为真实 Windows Sandbox：Windows 10.0.26100.0，hasDotnet=false、sdkFolder=false、editorInstalled=false。
原 `.wsb` 自动启动未生成结果；通过微软 Windows Sandbox CLI 共享任务产物并在 ExistingLogin 会话运行验证后，生成 environment-final.json、Cards-games.json、Hex-games.json 及退出码文件。
整体修复后重新发布并导出两示例，生成 environment-reviewed.json、FinalCards-games.json、FinalHex-games.json 及退出码文件；均再次通过。
这次没有把“移除 PATH”当作干净环境，也没有沿用 M1 的人工反馈替代 M2 验证。

正式 Play.exe 是 Windows GUI 子系统；没有 CMD 启动器，Lua worker 使用 CreateNoWindow。
构建去掉编辑器调试入口，实际调用 `--editor-debug --headless` 以退出码 2 拒绝。
必要加载/规则/设备错误显示在 GUI。依赖预检、目标目录冲突和插件附加步骤失败回滚测试通过。

完整测试入口已通过；包含脚本隔离/超限、CPU 错误、可见性、命令原子性、回放、包校验、插件依赖、作者文档和两模式调试。
最终构建零警告、零错误；29 个默认测试组和独立 export、m2-gate 测试组通过。
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
`artifacts/m2-sandbox/verify-reviewed.ps1` 是最终复验运行脚本；用 PowerShell CLI 启动，没有通过 GUI 输入终端命令。

## 使用范围

操作说明见 [编辑器指南](../editor-guide.md)。仅验收 Windows x64，仍有原生 HWND airspace 限制。
Lua 规则/CPU 为受控脚本，编辑器插件是宿主信任的官方代码；第三方运行时 WASM 不在 M2。
YAML 编辑经 YamlDotNet 转换到相同组件配置，磁盘主文件仍为 module.json。
日志为作者调试材料，包含隐藏真值与脚本版本，普通玩家只见席位投影与脱敏消息。

## Standards

未发现规范硬违规；技术栈、依赖边界与单人优先顺序符合仓库约定。

审核发现 JSON 草稿被其他面板覆盖、席位缓存未同步、表单把未修改的继承字段写成覆盖；三项均修复。
表单现在只存实际修改字段；原型其他字段继续传播。JSON 草稿保留，配置冲突明确拒绝应用。

主审另修复外部文件改动被保存覆盖、旧重载结果跨会话返回、停止后日志残留、回放仍可修改当前对局，以及导出插件可改写 Play.exe/版本清单的问题。
保存冲突保护磁盘内容；异步结果校验会话代际；回放只读；导出复核全部基础产物并拒绝编辑器／开发／链接内容。
导出扩展回到 UI 线程校验原注册对象，停用或重载后不执行旧扩展。

## Spec

发现未应用配置丢失、重命名席位后试玩失败、CPU 执行时暂停请求丢失，均修复并加入真实编辑器工作流验证。
启动前同步最新席位且保留有效选择；执行中的暂停排到当前命令结束后，不再自动越过下一命令边界。
回放期间拒绝提交、CPU 单步与重载，继续返回原会话；停止清除输入与旧日志。
未发现 M3/M4 越界实现。

回归覆盖文档外部冲突、原型继承、两种模式的回放只读、导出附加步骤篡改、实际窗口中的草稿／席位／忙时暂停操作。
完整复验输出位于 `artifacts/m2-final`；最终证据摘要同步到本目录的 m2-results.json。
