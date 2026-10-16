# 流程编辑器四审修复与验收

日期：2026-10-16。工作树仍未提交，基线为 `4ffb163`。本轮修改汇总／采集脚本、测试与文档；`src` 下生产源码逐文件哈希与三审快照一致。
“创作期间无引擎代码改动”仅指探针采集窗口前后哈希一致，不表示整个工作树相对 `4ffb163` 没有引擎修改。

## 四审意见逐条处理

| 编号 | 意见 | 决策、依据与结果 |
| --- | --- | --- |
| Q1 | Markdown 去重合计空白 | 完全采纳。[汇总脚本](../../scripts/summarize-flow-coverage.ps1) 显式累加有序字典字段，JSON 和 Markdown 共用数值。[脚本回归](../../scripts/test-flow-coverage-summary.ps1) 用跨生成类重复行验证工具总量 11/13、去重 11/12 和 Markdown；归档 XML 重建实测 1974/2128，不再生成空白。 |
| Q2 | 采集范围遗漏文档／项目／探针输入 | 完全采纳。[validate-flow](../../scripts/validate-flow.ps1) 从 Git 可见文件中采集 src、tests、samples、scripts、docs 下的 cs/csproj/ps1/config/md/props/targets，另包含 flow-cards／flow-minimal 全部输入及列明的根构建文件、ROADMAP 和 .gitattributes；排除 bin/obj 与历史 evidence。实际捕获 392 文件，包含报告列出的遗漏项；范围明写入 sourceScope，不再笼统称整个仓库全部源码。 |
| Q3 | 静态玩家未用于动作／边的结果没有诊断 | 不采纳新增诊断。[FlowController.Enter](../../src/Runtime/Flow/FlowController.cs#L82) 也会将进入脚本的结果交给 ApplyOutcome，要求其在 Results 中；该结果不一定由动作产生，也不需要触发离开节点。仅按动作和边判断“死声明”会误报合法的进入结果，任意 Lua 返回值也无法静态穷尽。本期保留非完成结果和声明的兼容性。 |
| Q4 | 非法 Lua 用例依赖文本 Replace | 完全采纳。[FlowTests](../../tests/Engine.Tests/FlowTests.cs) 使用独立 complete.lua 并显式指定 DecideScript；继续断言无后继完成被拒绝且状态原子回滚。 |
| Q5 | 生命周期测试 5 秒超时可能抖动 | 完全采纳。[FlowEditorTests](../../tests/Engine.Tests/FlowEditorTests.cs) 保留信号驱动的顺序断言，死锁 watchdog 放宽为 30 秒；finally 释放任务，避免前置断言失败时清理卡在等待信号。本轮实际回归通过，不声称能消除任意负载下的超时。 |
| Q6 | “无引擎代码改动”范围夸大 | 完全采纳。明确仅保证验证窗口未修改捕获输入；工作树相对规划基线的引擎修复仍存在。三审快照与四审生产源一致的核对另存证据。 |
| Q7 | ZIP 仅本机／审核者未重跑 Sandbox | 部分采纳。保留本机 ZIP 的定位和跨机需传递原文件的限制，区分作者三审执行与审核者的证据审计。本轮生产代码未变，只重跑受影响回归及 Editor 探针，不重新发布／导出／运行 Sandbox，也不把三审执行写成四审新执行；未上传外部服务。 |
| Q8 | 一审 90.03% 无出处 | 不采纳“证据已无出处”的判断。原始[一审汇总](evidence/flow-20261016/coverage-current-summary.json)仍记录 2096/2328，即 90.03%，并由[一审清单](evidence/flow-20261016/current-manifest.json)索引；补直接引用，不将历史口径用作当前覆盖率。 |

共 8 项：5 项完全采纳、1 项部分采纳、2 项不采纳。

## 四审实际验证

- Release 全解决方案 0 警告、0 错误；32 组回归、项目边界与新增汇总脚本回归通过：[构建](evidence/flow-20261016-round4/build.log)、[回归](evidence/flow-20261016-round4/regression.log)、[边界](evidence/flow-20261016-round4/boundaries.log)、[脚本测试](evidence/flow-20261016-round4/summary-test.log)。
- 两个 Editor 探针 `--verify-flow`、`--verify-runtime-documents` 均 exitCode=0、success=true：[来源与退出码](evidence/flow-20261016-round4/source.json)、[流程报告](evidence/flow-20261016-round4/ui.json)、[格式报告](evidence/flow-20261016-round4/formats.json)。本轮没有执行 `--verify-export`。
- 使用三审原始 XML 重新生成[汇总](evidence/flow-20261016-round4/coverage-summary.json)与[方法明细](flow-editor-coverage.md)：工具总量仍为 **2161/2334（92.59%）**，去重为 **1974/2128**，83 个低于 90% 的方法。逐文件／方法数据与三审同值同序；这是汇总修复，**不是新采集覆盖率**，详见[重建记录](evidence/flow-20261016-round4/summary-rebuild.json)。
- 392 个输入的采集字节保存在[源码快照](evidence/flow-20261016-round4/source-snapshot.zip)。验证期间 sourceUnchanged=true，收尾只更新文档；最终文档快照、生产源码同三审的比对及全部证据哈希见[四审清单](evidence/flow-20261016-round4/manifest.json)与[收尾记录](evidence/flow-20261016-round4/finalization.json)。

以下保留三审执行记录。三审 finalization 中的文档 final 哈希对应当时快照，不要求等于四审收尾后的工作树；三审产物、Sandbox 和覆盖率原始证据保持原字节。
## 三审意见逐条处理

| 编号 | 意见 | 决策、核实与修改 |
| --- | --- | --- |
| T1 | 节点类型字面量残留 | 完全采纳。[GameRuntime](../../src/Runtime/GameRuntime.cs) 的初始／恢复加载及 [FlowVerification](../../src/Editor/FlowVerification.cs) 全部使用 FlowNodeKinds；源码中四类节点的 Kind 比较和赋值已无重复字面量。测试夹具仍可使用协议字符串。 |
| T2 | 字符长度分支冗余 | 完全采纳。[FlowFile.Parse](../../src/Runtime/Flow/FlowDefinition.cs) 仅按 UTF-8 字节数限制，保留恰限、超限与中文输入测试。 |
| T3 | 等待结果被强制要求后继 | 完全采纳。[FlowValidation](../../src/Runtime/Flow/FlowValidation.cs) 只对静态完成动作要求潜在后继；等待动作的结果须声明但无需连边。Lua 无法静态判断 complete，无后继结果给 info，实际完成时仍由运行事务拒绝并回滚。[FlowTests.Players](../../tests/Engine.Tests/FlowTests.cs) 覆盖静态／Lua 的 stay 等待、完成结果共用 stay、未声明等待结果及 Lua 无边完成的回滚。 |
| T4 | 旧启动脚本字节缺失 | 完全采纳。恢复并归档 [c796e44d 源码快照](evidence/flow-20261016-round2/validate-flow-sandbox-source.ps1)，SHA-256 与旧 source.json 完全一致；注明是按差异恢复的采集版本，不冒称成功运行版本。三审启动前另复制实际启动／客体脚本，清单同时记录两者哈希。 |
| T5 | 文档及属性不在来源清单 | 完全采纳。[validate-flow](../../scripts/validate-flow.ps1) 当时仅采集 src、tests/Engine.Tests、scripts 下的 C#／项目／PowerShell／覆盖率配置及列明的根文件／文档（四审扩展此范围），保存原始字节并检查验证期间未变。当轮最终文档更新另存快照与差异记录，避免把事后修改称为采集时源码。 |
| T6 | 证据未跟踪导致规则无效／克隆 404 | 不采纳作为代码缺陷的判断。`git check-attr text -- <未跟踪证据>` 实测为 unset，-text 对这些路径已可解析。未提交文件不会出现在别处克隆是事实；本任务未自动提交或发布，源码与证据应在后续提交时一并纳入，不能把工作树验收当作已发布证据。 |
| T7 | 取消注释的 this token 含糊 | 完全采纳。[FlowController](../../src/Runtime/Flow/FlowController.cs) 明确 Cancel 只触发 stop，Advance 设置 activeCancellation；仍由会话 commands 锁串行调用。 |
| T8 | 总行数与逐文件合计不同 | 完全采纳。[汇总脚本](../../scripts/summarize-flow-coverage.ps1) 说明了工具总量和按文件行号去重的区别，但当时 Markdown 去重数生成为空；四审修复并补回归。 |
| T9 | 探针只有整体 success | 完全采纳。[ExportVerification](../../src/Editor/ExportVerification.cs) 在各断言成功后记录 10 个 passed 项，清理结束后才写报告；FlowVerification 记录创作、两类对局、重开、插件身份及生命周期结果。进程退出码仍独立检查。 |
| T10 | Sandbox 缺当前执行复验 | 完全采纳。对三审重新发布和导出的产物启动新的空白 Sandbox，使用冻结脚本与文件清单核验；实际结果见下节。审核者是否另行重跑与本次执行证据分别表述。 |
| T11 | 创作闭环与两插件独立识别证据不足 | 完全采纳。探针核对 Editor 的 author.flow 和 Runtime 的 flow 目录互不混入对方 ID；停用作者插件仍保留会话。正式导出改为消费三审真实控件创作、保存并重开的工程，随后进入 Sandbox；采集前后源码一致，无创作期间改写引擎代码。 |
| T12 | 导出／发行字节不可复算 | 部分采纳。保存完整发行与导出 ZIP，并逐文件核对 ZIP 内容和原清单；创作工程及源码快照纳入仓库证据目录。两个约 108 MiB 的二进制 ZIP 保留在本机 artifacts，未将大体积发行物放入 Git，也未上传外部服务；其他机器需取得这些归档才能复算相同产物哈希，重新编译不承诺字节相同。 |

共 12 项：10 项完全采纳、1 项部分采纳、1 项不采纳。三审已确认成立的 DiskEditMode／WorkspaceEditMode 区别、bool? 真／假／未知语义、退出码消费与轨迹修订裁决保持不变；未扩大为消除编辑器所有同步等待。

## 三审执行结果

| 验证 | 结果 | 原始证据 |
| --- | --- | --- |
| Release 全解决方案 | 0 警告、0 错误 | [build.log](evidence/flow-20261016-round3/build.log) |
| 回归 | 32 组 PASS | [regression.log](evidence/flow-20261016-round3/regression.log) |
| 依赖边界 | 通过 | [boundaries.log](evidence/flow-20261016-round3/boundaries.log) |
| 真实控件创作及两插件 | exitCode=0，7 项通过 | [UI 分项报告](evidence/flow-20261016-round3/ui.json)、[截图](evidence/flow-20261016-round3/ui.png)、[创作工程](evidence/flow-20261016-round3/authored-project.zip) |
| 既有格式生命周期 | exitCode=0，success=true | [formats.json](evidence/flow-20261016-round3/formats.json) |
| 正式发布与创作工程导出 | self-contained win-x64；导出 exitCode=0，10 项通过 | [发布日志](evidence/flow-20261016-round3/publish.log)、[导出分项报告](evidence/flow-20261016-round3/export.json) |

三个探针退出码、具体导出工程路径、源码哈希和 `sourceUnchanged=true` 见[来源报告](evidence/flow-20261016-round3/source.json)。
[源码快照](evidence/flow-20261016-round3/source-snapshot.zip)保留所有清单文件的采集字节；该快照是基于上述 Git 基线的验证输入，不是独立的完整仓库镜像。
`flow-delivery` 当前 PASS 还覆盖 `GamePackage.Pack → 删除源目录 → GameRuntime.Load(package)` 及快照 CPU 对局，不依赖历史 Sandbox 推断该行为。

Sandbox 宿主退出码 **0**，实例已自动关闭；客体核对 **571 个文件**，完成种子 71／72／73 的批量对局（rerun/replay 全通过）及 **2 局 GUI 对局**，两个 Player 进程退出码均为 0。
[无全局 .NET 环境](evidence/flow-20261016-round3/sandbox-environment.json)、[客体结果](evidence/flow-20261016-round3/sandbox.json)、[批量日志](evidence/flow-20261016-round3/sandbox-batch.log)、[GUI 报告](evidence/flow-20261016-round3/sandbox-player.json)及[宿主校验报告](evidence/flow-20261016-round3/sandbox-host.json)均已归档。
`game.bgm` SHA-256 为 `5b4cbe0556a9d9fe8ffabdec907928e165e6219fadcf25f80cdfa3afc886d2a0`，导出清单 SHA-256 为 `942d935d5b57b81b8924ac5e26f1272d898569990702be623a0e0a4a0412c56c`。
[清单](evidence/flow-20261016-round3/export-manifest.json)同时绑定来源报告、实际启动脚本和客体脚本；两脚本字节与来源快照一致。
验证结束后的文档变化及宿主退出码观察记在[收尾记录](evidence/flow-20261016-round3/finalization.json)，采集前／后的文档字节均保留。

## 覆盖率口径

三审工具总量为 **2161/2334 行（92.59%）**；按文件与行号去重后合计 **1974/2128 行**。
工具按类汇总时，同一源码行可能同时出现在生成类中；两种计数都可从 XML 复算，不互相替代。
完整列出 **83 个低于 90% 的方法条目**（包括生成状态机与闭包），见[方法明细](flow-editor-coverage.md)。
[原始 XML](evidence/flow-20261016-round3/coverage.xml)、[汇总 JSON](evidence/flow-20261016-round3/coverage-summary.json)、
[工具版本／哈希](evidence/flow-20261016-round3/coverage-tool.json)、[采集日志](evidence/flow-20261016-round3/collection.log)属于同一次执行。

范围由 [flow-coverage.config](../../scripts/flow-coverage.config) 的正向 Include 和显式 Exclude 决定，包含流程及事务、状态、回放、会话、作者工作区和相关导出／生命周期文件。
FlowVerification 被显式排除，RuntimeDocumentVerification／ExportVerification 不匹配 Include，同样不计入生产分母。
这不是全仓覆盖率，也不表示每个方法达到 90%；上一轮 2152/2330、86 项明细保留为历史，不能与三审方法表混用。

## 复跑与证据边界

前置：PowerShell 7、Git、global.json 指定 SDK、Windows UI 环境、Visual Studio Code Coverage 组件及已启用 Windows Sandbox。
覆盖率工具可由 PATH／vswhere 自动发现，也可传 `-CoverageTool` 完整路径。Sandbox 须使用没有既有任务的空白实例。

1. `pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/player-flow-new`
2. `pwsh -NoProfile -File scripts/measure-flow-coverage.ps1 -Output artifacts/flow-check-new -Release artifacts/player-flow-new`
3. `pwsh -NoProfile -File scripts/validate-flow-sandbox.ps1 -ExportDirectory artifacts/flow-check-new/export-check/export -Output artifacts/flow-sandbox-new -SourceEvidence artifacts/flow-check-new/result.json`
4. `pwsh -NoProfile -File scripts/summarize-flow-coverage.ps1 -InputFile artifacts/flow-check-new/coverage.xml -OutputFile artifacts/flow-check-new/summary.json -Markdown docs/validation/flow-editor-coverage.md`

所有输出使用新路径。第二步先采集输入字节，再运行回归、控件创作和正式导出；第三步冻结实际启动与客体脚本，仅映射导出产物、脚本快照和证据输出，不映射作者工程。
产物 ZIP 为本机归档，路径、大小、SHA-256 及逐文件验证结果记录在[产物归档清单](evidence/flow-20261016-round3/artifact-archives.json)。仓库中的清单不能替代 ZIP 本体。
三审全部归档文件及最终文档差异由[三审证据清单](evidence/flow-20261016-round3/manifest.json)索引。

[一审证据](evidence/flow-20261016/current-manifest.json)、[早期历史证据](evidence/flow-20261016/historical-manifest.json)、[二审证据](evidence/flow-20261016-round2/manifest.json)保留原始执行结果。
旧 FlowVerification／RuntimeDocumentVerification／ExportVerification 曾在写 success 后因清理异常退出；[异常事件](evidence/flow-20261016/ui-failed-cleanup-events.json)已索引，历史 success JSON 不能单独证明正常退出。
二审首个 Sandbox 因 5 分钟冷启动等待超时，不计作完整通过；该轮改为 10 分钟后复跑通过，但脚本采集／运行版本的历史限制仍在原宿主记录中披露。三审通过实际执行前的脚本快照消除这一缺口。

首期仍为平面流程；任意 Lua 动态引用及终止性不做静态证明，不包括流程热替换、Lua 断点、嵌套或并行状态。