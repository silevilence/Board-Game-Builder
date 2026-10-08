---
name: editor-cordis
description: 新增、修改或修复本项目编辑器功能时使用，按现有 CordiSharp（CORDIS）内置插件体系实现；不适用于纯引擎、共享 Runtime 或 Player 功能。
---

# 编辑器功能规范

## 必要约束

- 所有编辑器功能都由内置 CORDIS 插件提供；本项目的实际实现是 CordiSharp。修改已有功能优先扩展所属插件，新增功能按完整能力拆分，不按按钮或控件拆插件。
- 使用 `EditorPluginDefinition`，在 `BuiltinPlugins.Install` 装配，声明稳定 ID、API 及 `Provides` / `Requires`。通过 `scope.Service<T>` / `scope.Provide` 协作，不绕过声明访问服务或另建插件生命周期体系。`EditorHost` 仅负责启动、根上下文、装配与启动失败诊断，不承载功能逻辑或私有功能回调。
- 面板经 `PanelContributions.Register(scope, id, title, factory)` 贡献 Avalonia 控件，工作区统一挂载；命令经 `EditorCommands.Register` 注册，菜单、工具栏和快捷键共用执行体与可用状态，遵守文本编辑和视口焦点边界。MonoGame 只承担场景视口绘制，周边 UI 使用 Avalonia，遵守原生视口 airspace 限制。
- 文档、未保存草稿、选择及 Undo 归 `documents`，试玩状态归 `sessions`；面板重建只重新连接，不丢数据或隐式重开对局。表单输入（含无效文本）通过 `DocumentWorkspace.Form/EditForm` 保存，沿用应用、放弃、基线冲突及保存前协调规则。
- 注册、订阅和资源归所属插件：使用 `scope.Own`、`scope.On` 或 `scope.Context.Effect` 清理；UI 创建与释放在 UI 线程。异步工作销毁时取消并等待结束，忽略迟到结果；加载失败及重复启停不残留面板、命令或回调。活动状态或长操作需要时用 `scope.Guard` / `app.Retain` 阻止不安全停用；面板移除不等于程序集已回收。
- 此规范仅约束编辑器能力。引擎、规则或共享运行语义仍放在对应层；所需编辑 UI 再由内置插件提供。Kernel 保持无外部依赖；共享 Runtime 和发行 Player 可依赖固定 CordiSharp 核心，禁止 Editor/Core 及 CordiSharp.Plugins.Avalonia 等编辑器专用依赖。编辑器与每局游戏使用独立根上下文、目录和生命周期。
- 游戏文档通过 `IGameAuthorFormat` 读取／修改结构化定义；启动入口是 `game.start.json`。格式专用面板用 `FormatPanel` 管理子上下文，缺少作者格式接口时提示并保留通用文件编辑与试玩。草稿和会话仍由基础插件持有。

## 按需定位

以下路径相对项目根目录；只读取本次变更涉及的入口，不必遍历全部文件。

| 关注点 | 入口 |
| --- | --- |
| 需求及插件边界 | `ROADMAP.md` 的当前约定与 M2.5；细节见 `docs/editor-plugins.md` |
| 内置插件归属、装配及面板工厂 | `src/Editor/BuiltinPlugins.cs`；同目录对应 `*Panel.cs` |
| 定义、依赖、scope 与生命周期 | `src/Editor.Core/EditorApplication.cs` |
| 面板挂载、命令与附加贡献 | `src/Editor/WorkspaceShell.cs`、`src/Editor.Core/EditorCommands.cs`、`src/Editor/CommandSurface.cs` |
| 文档／草稿、会话、视口、导出 | `src/Editor.Core/DocumentWorkspace.cs`、`src/Editor/SessionController.cs`、`src/Editor/EditorViewports.cs`、`src/Editor/ExportWorkflow.cs` |
| 验证 | `tests/Engine.Tests/CordiTests.cs`；真实 UI／会话／导出验证参数见 `src/Editor/Program.cs`，实现见同目录 `*Verification.cs` |

验证聚焦改动行为；涉及注册或生命周期时覆盖失败清理和重复启停，涉及状态时覆盖面板重建后的保留。需求变更同步 ROADMAP；不把已有 M2 旧实现约定或初始设计留档当作当前插件规范。
