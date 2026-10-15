# 引擎状态机流程插件验收

日期：2026-10-15。范围：ROADMAP 的引擎组 8 项；Editor 可视化插件未开始。

## 分项验证

| 任务 | 验证 |
| --- | --- |
| 文件契约 | flow-contract：解析往返、未知版本、必需字段、未知字段 |
| 结构校验 | flow-validation：封闭环、恒假出口、流向封闭环、正常循环和玩家等待 |
| 插件及事务 | flow-runtime：系统身份、隐藏实体操作、失败回滚、重复提交、查询不重入 |
| 脚本分支 | flow-systems：最新候选状态、默认／冲突分支、私有上下文、错误脱敏、确定性 |
| 玩家调度 | flow-players：非当前回合席位、重复操作、过期拒绝、CPU 同语义及权限边界 |
| 自动保护 | flow-protection：节点预算、取消、显式续行、CPU 总动作预算 |
| 记录与宿主 | flow-restoration：等待／系统边界／终局恢复、内容变化拒绝、联网重连及共享试玩／Player |
| 作者及交付 | flow-delivery：12 个固定种子、确定性平局、真人加 CPU、作者版本冲突、CLI 调试记录、删除源目录后包／快照运行 |

上述 8 个测试组均通过；开发过程中同时通过既有 replay、runtime-host、startup、lua、sandbox、cpu、
local-table、worker、online-reconnect、runtime-recording、author-operations、author-docs、author-runs、author-validation。
CLI 实际执行 project.validate 无诊断，run.cpu 两个种子均终局且重复／回放一致，docs flow 可发现说明。

## 独立 Player 实测

使用 `scripts/publish-player.ps1 -Output artifacts/player-release-flow-20261015` 构建正式 self-contained Player，
再通过正式 `Player.exe export` 导出到 `artifacts/export-flow-20261015`。
导出目录的 Play.exe 直接加载 game.bgm，GUI smoke 实际完成 2 局真人席位动作加 CPU 对局，重开后终局一致。
机器报告：`artifacts/flow-player-smoke-20261015.json`，success=true、games=2、真人动作 6 次。
不携带 Editor 功能插件；本次证据来自当前 Windows 主机，不声称在另一台干净机器上执行。

## 边界

第一版不组合 WASM 或秘密收集调度，不提供嵌套／并行状态、实时计时器及图热替换。
未知 Lua 条件的终止性仍由作者负责；自动预算暂停不是数学上的死循环证明。
旧纯 Lua 模式保留。权威记录包含私有上下文，只供作者／服务器所有者；网络客户端只得到投影。

## 整体审核与回归

整体审核覆盖需求、仓库边界、事务／回放一致性、可见性、文件资源限制、取消和作者编辑模式。
审核修复布尔比较与逻辑短路、恒真分支遮蔽出口、上下文清空前的大小检查、文件读取前的预算检查、
重复 JSON 键和无效脚本引用诊断、玩家随机输入的可见性、导出记录与提交的同步、失败恢复的续行限制。
流程作者读写通过 IGameAuthorFormat 适配，继续使用 ProjectView／IProjectEditMode；无效原文件也可显式替换。
新增 flow-review 验证实际调用中的取消、失败节点原子性、超大上下文拒绝和玩家随机输入隔离。

跨模块共选择 54 组，覆盖 9 个 flow 分组及内核、Lua、旧规则、打包、运行插件、作者、联网和 WASM。
组合命令前 53 组通过，最后的既有 wasm-isolation 原生异常退出，没有托管错误输出；
显式运行 `dotnet --fx-version 10.0.12 tests/Engine.Tests/bin/Release/net10.0/Engine.Tests.dll wasm-isolation` 通过。
此现象已有 cli-author-review／m3.5-review 记录，相关 WASM 源码与测试本次未变；不声称组合命令一次全部通过。
最终使用同一明确运行时补跑审核改动涉及的流程分组，并复验正式 Player。

最终结果：Release 全解决方案构建 0 警告、0 错误，`scripts/check-boundaries.ps1` 通过。
`artifacts/flow-final-tests.log` 保存 9 个流程组、author-docs、runtime-recording、wasm-isolation 的通过结果；
`artifacts/flow-delivery-final.log` 补验仅有流程 Lua 时缺少 worker 的发行依赖拒绝、控制器冲突和未知版本。
最终正式发行位于 `artifacts/player-release-flow-final`，导出游戏位于 `artifacts/export-flow-final`；
`artifacts/flow-player-final.json` 再次报告 success=true、games=2、真人动作 6 次。流程内容随 game.bgm 独立运行。
本组无剩余功能验收阻塞；上述默认宿主下 WASM 探针异常作为既有环境限制保留，未修改隔离策略。
