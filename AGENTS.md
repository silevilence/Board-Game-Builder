## 需求与开发顺序

- 当前需求与验收以 [ROADMAP.md](ROADMAP.md) 为准；讨论形成变更后同步更新对应条目。
- [初始需求与设计](docs/桌游引擎需求与设计.md) 仅作历史留档，不修改，也不作为最终任务依据；其中的“冻结决策”同样不覆盖后续讨论与 ROADMAP。
- 先完成本地单人闭环与验证，多人规则先用 CPU 队友／对手补齐；联机任务须等 ROADMAP 的单人闭环门槛通过后再开始。
- 首选技术栈：C# 内核、Avalonia 编辑器、MonoGame 2D 视图、Lua 规则与 CPU 脚本。替换既定选型前先讨论并更新 ROADMAP。

## 协作规范入口

- 需求在 ROADMAP.md，工单在 `.scratch/<功能>/issues/`；详见 [工单规范](docs/agents/issue-tracker.md)。
- 工单使用五个默认分类状态；详见 [状态规范](docs/agents/triage-labels.md)。
- 领域术语在 CONTEXT.md，架构决策在 `docs/adr/`；探索代码前按 [领域文档规范](docs/agents/domain.md) 读取，文件不存在时直接继续。
