## 需求与开发顺序

- 当前需求与验收以 [ROADMAP.md](ROADMAP.md) 为准；讨论形成变更后同步更新对应条目。
- [初始需求与设计](docs/桌游引擎需求与设计.md) 仅作历史留档，不修改，也不作为最终任务依据；其中的“冻结决策”同样不覆盖后续讨论与 ROADMAP。
- 先完成本地单人闭环与验证，多人规则先用 CPU 队友／对手补齐；联机任务须等 ROADMAP 的单人闭环门槛通过后再开始。
- 首选技术栈：C# 内核、Avalonia 编辑器、MonoGame 2D 视图、Lua 规则与 CPU 脚本。替换既定选型前先讨论并更新 ROADMAP。

## 引擎与游戏的边界

- 引擎只能实现可复用的通用功能，不得与某个具体游戏绑定。此要求适用于 Kernel、共享 Runtime、视图、Player 和编辑器宿主，不能通过将游戏专用逻辑移到其他引擎层来规避。
- 具体游戏的规则、胜负条件、CPU 策略、流程、名称、帮助文案及展示约定，应由游戏模块、脚本、资源或配置承载；示例游戏是引擎功能的使用者，不是引擎的特殊分支。
- 不得根据游戏名称、模块 ID、示例路径或某个游戏专用的实体／属性约定，在引擎中硬编码行为；也不得借主题或样式开关隐式启用游戏专用规则、流程或交互。
- 示例需要引擎尚未提供的能力时，先将需求抽象为职责明确、可独立配置的通用功能，再由示例组合使用；功能命名、接口和默认行为不得包含具体游戏语义。

## 协作规范入口

- 需求在 ROADMAP.md，工单在 `.scratch/<功能>/issues/`；详见 [工单规范](docs/agents/issue-tracker.md)。
- 工单使用五个默认分类状态；详见 [状态规范](docs/agents/triage-labels.md)。
- 领域术语在 CONTEXT.md，架构决策在 `docs/adr/`；探索代码前按 [领域文档规范](docs/agents/domain.md) 读取，文件不存在时直接继续。
- 涉及编辑器功能新增、修改或修复时，自动使用项目技能 [editor-cordis](.agents/skills/editor-cordis/SKILL.md)；纯引擎、共享 Runtime 或 Player 功能不触发。

## 运行时与作者格式

- 正常加载使用 `GameRuntime.Load`，打包使用 `GamePackage.Pack`；`DirectoryModule.Load` 和旧 `ModulePackage` 仅用于显式历史解析／迁移，不增加静默回退。
- 游戏入口为 `game.start.json`。包身份与插件启用配置仅在此声明；定义路径由加载插件配置。参考 [运行插件协议](docs/runtime-plugins.md)。
- Runtime 允许固定 CordiSharp 核心，与编辑器分离根上下文及插件目录；不得引用 Editor、Editor.Core 或 CordiSharp.Plugins.Avalonia。运行纤程不捕获 UI 同步上下文。
- 作者结构化功能通过 `IGameAuthorFormat` 工作；不支持的格式保留通用文件编辑与试玩，不在基础文档固定解析 `module.json`。格式面板由子插件上下文释放，跨格式的迟到通知不得读取新文档。
- 独立作者契约与领域操作位于 Authoring，不引用 Editor/Core 或 Avalonia；Cli 通过作者插件发现操作。领域操作读取 `ProjectView`，通过 `IProjectEditMode` 提交并核对来源／版本，不能绕过编辑模式直接修改源工程。
- 作者 `author.profile.json` 与运行入口分离。GUI 模式绑定工作区和文档代次，使用 DocumentWorkspace 的草稿及 Undo；校验、运行、导出不隐式保存，连接失败不回退磁盘模式。详见 [作者工具协议](docs/author-cli.md) 和 [ADR-0002](docs/adr/0002-author-project-modes.md)。
