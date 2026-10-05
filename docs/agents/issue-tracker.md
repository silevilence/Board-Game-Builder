# Issue tracker: Local Markdown

- ROADMAP.md 是需求与规格的唯一来源；工单引用对应条目，不另建 spec.md。
- 每个工单单独保存为 .scratch/<feature-slug>/issues/<NN>-<slug>.md，
  从 01 开始编号。
- 工单顶部使用 Status: 记录分类状态，取值见 triage-labels.md。
- 讨论追加在工单的 ## Comments 下。
- 发布需求或规格时，更新 ROADMAP.md；发布实施工单时，创建独立工单文件。
- 获取任务时，读取指定工单及其关联的 ROADMAP.md 条目。
