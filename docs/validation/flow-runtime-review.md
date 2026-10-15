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
