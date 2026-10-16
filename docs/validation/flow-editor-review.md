# 状态机可视化设计 Editor 插件验收

日期：2026-10-16。范围：ROADMAP 的 Editor 组 6 项。使用 start-tasks 分项实施、本地提交，
再按 review-task 的需求、质量、规范三方面复核；未引入第二套流程执行器。

主要实现：[作者操作](../../src/Authoring/FlowDesign.cs)、[工作区模型](../../src/Editor.Core/FlowWorkspace.cs)、
[插件与面板](../../src/Editor/FlowPanel.cs)、[画布](../../src/Editor/FlowCanvas.cs)、
[属性与脚本](../../src/Editor/FlowInspector.cs)。用户入口见[创作说明](../flow-editor.md)，
插件协议见[贡献说明](../editor-plugins.md)。

## 需求与证据

| 任务 | 实现与验证 |
| --- | --- |
| 作者模型与插件接入 | `author.flow` 版本/API 1；创建入口及流程文件在一次作者操作中提交；flow-editor-model 验证跨定义格式、来源代次、纯设计数据不改变运行身份 |
| 四类节点画布与连线 | flow-editor-canvas 验证节点/关联边、循环、Undo/Redo、保存重开；真实 UI 点击创建、拖动节点、端口连线、Delete、滚轮、平移及诊断定位 |
| 属性、席位与 Lua | flow-editor-properties 验证无效字段保留、版本冲突、脚本原子创建与撤销，四类模板实际组成可结束的对局；名称不改变稳定 ID 或运行身份 |
| 校验与定位 | flow-editor-validation 对比共享 CLI/Editor 结果，验证恒假出口、整组无终局路径、未知条件与缺脚本定位；修复并切换工程后无旧结果 |
| 试玩位置与轨迹 | flow-editor-playback 验证作者/玩家边界、版本核对；真实 UI 验证玩家等待、混合/全 CPU 对局、预算暂停及显式继续，重建不重入、草稿变更撤销高亮 |
| 生命周期与独立交付 | flow-editor-conflicts 验证多表单、双格式草稿、外部修改与取消；真实 UI 验证子上下文释放、重复启停、失败工厂清理；草稿正式导出和 Windows Sandbox 独立运行见下文 |

真实 UI 验证使用 `Editor.exe --verify-flow`，不是仅构建模型：通过 Avalonia 的按钮事件、
属性 TextBox 和指针事件，从空流程重新构建卡牌示例的 8 个节点、8 条边，并经已有布局面板应用布局。
实体及游戏短脚本来自 flow-cards 夹具；另通过创建脚本控件生成并关联入口模板。
验证没有新增引擎游戏分支，也不声称从空白工程逐字重新输入了全部实体与样例脚本。

独立 UI 报告为 [最终流程验证](../../artifacts/flow-editor-coverage-accepted.json)，同名 `.png` 已检查；
同名 `.project` 为保存后可继续编辑的工程。它还覆盖了预算暂停、显式继续、
创建流程后的撤销重做、字段保留、快速格式切换和失败激活清理。

## 导出与干净 Windows

`scripts/publish-player.ps1 -Output artifacts/player-release-flow-editor` 构建正式 self-contained 发行。
真实 Editor 的 `--verify-export` 使用内存入口版本草稿，经 ExportWorkflow 正式导出，
对比生成包的运行指纹与声明来源，确认源目录字节、草稿 Dirty 和当前文档不变；
同时覆盖缺依赖、附加步骤停用保护及临时快照清理失败。
报告：[正式导出结果](../../artifacts/flow-editor-export-final/result.json)，success=true。

导出路径：`artifacts/flow-editor-export-final/export`。关闭 Editor 后，Play.exe 在开发机
GUI smoke 完成 2 局、6 次真人动作；见[Player 结果](../../artifacts/flow-editor-player.json)。

随后将上述导出目录只读映射到新 Windows Sandbox，输出目录单独可写，网络禁用。
客体检查没有 dotnet 命令及 Program Files 下的全局 .NET 安装，再直接启动发行 Play.exe。
[Sandbox 结果](../../artifacts/flow-sandbox/result.json)记录 Windows NT 10.0.26100.0、globalDotnet=false、success=true；
[客体 Player 结果](../../artifacts/flow-sandbox/player.json)记录 2 局和 6 次真人动作。发行不携带 Editor、Editor.Core 或 CordiSharp Avalonia 插件。
验证完成后已关闭本次临时 Sandbox。

## 整体审核与修复

未遗留已知 Critical/Important 功能问题。审核过程中修复并回归了：

- 🟠 纯视图操作会重写非规范化流程 JSON，改变运行身份：现在保留原始流程字节。
- 🟠 干净表单/脚本缓存会阻断作者 Undo：版本只计入内容及脏文件草稿；未应用表单仍阻止 Undo 作者操作。
- 🟠 多个字段草稿相互阻塞：应用一个表单保留其他输入，并在其他表单应用时检查原版本。
- 🟠 格式专用面板生命周期：使用独立格式谓词的子 fiber，旧格式通知在读取文档前过滤。
- 🟠 既有插件释放与同步运行快照加载死锁：线程栈确认 CordiSharp 调度线程同步等待 UI，
  UI 又等待运行加载。初始化及 Own 清理改用可等待的异步 UI 派发；仍在 UI 线程释放并等待完成。
  [cordi-dispatch](../../tests/Engine.Tests/CordiTests.cs) 在修复前稳定超时、修复后通过；
  原真实格式重载命令也通过。FormatPanel 的排队、初始化及子上下文释放同时遵守取消边界。
- 🟠 既有 Editor 导出隐式保存：改为读取一次 ProjectView 并物化 ProjectSnapshot，保留字段冲突和来源证据。
- 🟡 脚本创建路径/角色最初由控件持有：改为工作区字段草稿，增加应用和放弃路径。

预算 UI 验证曾因测试夹具使用 `/turn` 而失败；运行条件根是节点输入，已改为 `/state/turn`，
并在共享帮助中说明。不将这次失败伪记为运行时缺陷。

死锁定位证据为 [等待线程栈](../../artifacts/format-hang-stack.txt)。此前只添加 FormatPanel 取消
边界时仍能复现，未将偶然通过视为根因已解决；最终使用独立上下文可继续调度的确定性测试锁定修复。

## 规范核对

| 项目 | 结果 | 依据 |
| --- | --- | --- |
| 引擎与游戏边界 | 通过 | 引擎没有按示例名称、路径或实体约定分支；卡牌逻辑仍为示例配置与脚本 |
| 插件所有权 | 通过 | 独立 author.flow 能力；格式子上下文清理 UI，基础 documents/sessions 保留状态 |
| 作者操作 | 通过 | ProjectView、来源/代次/版本核对及 IProjectEditMode；无源文件直接写入 |
| 草稿与隐藏信息 | 通过 | 未应用字段明确拒绝运行；作者轨迹与普通席位投影分开；旧版本不高亮 |
| 正常加载与发行 | 通过 | GameRuntime.Load、正式发行导出；独立 Player 不携带作者插件 |
| 公共接口兼容 | 通过 | 保留 EditorApplication、FormatPanel 与 ForContext 原重载 |
| 校验与覆盖率 | 见下文 | 共享 CLI/Editor 诊断、真实 UI 和隔离 Windows 验证 |

## 回归与覆盖率

[初轮回归日志](../../artifacts/flow-editor-regression.log)的 28 组全部通过：6 个 flow-editor 分组、9 个既有 flow 分组，
以及 cordi、debug-play、documents、packages、replay、cpu、作者操作/校验/工作区/外部协调、
startup、runtime-recording、runtime-entry。后续工作区缓存修复另补跑 flow-editor-model、
author-workspace、author-external；模板与名称修改另补跑 flow-editor-properties，均通过。

最终代码再跑上述 28 组加 cordi-dispatch，共 **29 组通过、0 失败**；真实流程 UI、
[草稿正式导出](../../artifacts/flow-editor-coverage-export-complete/result.json)和
[既有格式生命周期](../../artifacts/flow-editor-format-complete.json)均 success=true。
Release 全解决方案构建 0 警告、0 错误，[边界检查脚本](../../scripts/check-boundaries.ps1)通过。

Microsoft.CodeCoverage.Console 18.6 的最终合并结果为 **1176/1245，94.46% 行覆盖率**，
达到本次审核默认 ≥90% 阈值。范围为 Authoring/Editor.Core/Editor/Runtime 的 Flow*.cs
及 ExportWorkflow、ExportPanel、FormatPanel、EditorApplication、EditorHost；排除 FlowVerification
验收代码。这是指定生产代码范围的结果，不是全仓覆盖率，也不表示每个函数均达到 90%。
完整未达 90% 的方法（含编译器生成回调）见[覆盖率明细](flow-editor-coverage.md)。

采集使用同一 Release 二进制；[主采集脚本](../../artifacts/flow-coverage.ps1)完成 29 组测试和真实流程 UI。
该次脚本误把正式 Player 路径改名，随后的导出检查明确失败；纠正路径后用
[补充采集脚本](../../artifacts/flow-coverage-finish.ps1)完成导出和旧格式验证，并合并两份覆盖率。
最终数据为 [Cobertura XML](../../artifacts/flow-editor-coverage-complete.xml)，没有把失败检查计为通过。
工具命令为 `collect -s artifacts/flow-coverage.xml -f cobertura -o <结果.xml> pwsh -NoProfile -File <采集脚本>`，
随后 `merge <主结果.xml> <补充结果.xml> -f cobertura -o artifacts/flow-editor-coverage-complete.xml`。
本地 artifacts 证据由验证命令生成，不纳入版本库。

## 范围边界

首期属性面板使用共享契约 JSON，附类型说明、诊断和独立脚本控件；没有另建每个字段的游戏专用表单。
任意 Lua 内的动态引用与终止性不做静态证明。没有热替换图、Lua 断点、嵌套或并行状态。
不可运行的图可保留内存草稿，正常文档保存仍要求运行校验通过；原始文本编辑始终可用。

## 结论

六项任务均可标记完成，无剩余阻塞项。需求闭环、代码质量和项目边界检查通过；
发现的问题已按 start-tasks 授权修复并回归。ROADMAP 在原位置勾选，不搬移任务。
