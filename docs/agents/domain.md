# Domain Docs

采用单上下文布局：
- 根目录 CONTEXT.md：领域术语与上下文。
- docs/adr/：架构决策记录。

探索代码前，读取 CONTEXT.md 和与当前工作相关的 ADR。
文件不存在时直接继续，不主动要求创建；由 domain-modeling 技能
在术语或决策明确后按需创建。

输出中使用 CONTEXT.md 定义的术语，避免被明确排除的同义词。
发现术语缺口时记录下来，供 domain-modeling 处理。
方案与现有 ADR 冲突时，明确指出冲突及重新讨论的理由。
