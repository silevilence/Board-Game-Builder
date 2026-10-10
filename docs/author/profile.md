# 作者 profile 与编辑模式

选择顺序：显式 `--profile <名称>`、工程 `author.profile.json` 的 `defaultProfile`、内置默认。不进行隐式合并，指定名称不存在会报错。

```json
{"defaultProfile":"disk","profiles":{
  "disk":{"format":1,"plugins":[{"id":"edit.disk"},{"id":"author.files"},{"id":"author.module"},{"id":"author.state"},{"id":"author.configuration"},{"id":"author.resources"},{"id":"author.validation"},{"id":"author.run"},{"id":"author.export"}]},
  "gui":{"format":1,"plugins":[{"id":"edit.gui"},{"id":"author.files"},{"id":"author.module"},{"id":"author.state"},{"id":"author.configuration"},{"id":"author.resources"},{"id":"author.validation"},{"id":"author.run"},{"id":"author.export"},{"id":"author.workspace"}]}
}}
```

插件选择省略 version/api 时均为 1。最小必需核心 author.core 由宿主补齐；每个 profile 必须恰好一个编辑模式。模式不会因连接失败而互相回退。插件以完整能力贡献操作，缺少可选插件可通过 `plugins`、`operations`、`describe` 和本地 `docs` 发现。

磁盘模式每次调用重新读文件。读取视图包含明确来源和版本；提交检查读取文件、目录枚举、目标基线及新增目标不存在。与外部编辑器不承诺跨文件全局原子性。磁盘成功回执的 saved 为 true。

GUI 模式需 Editor 启用 author.workspace 和 author.connection 插件。`bgb.exe workspaces` 列出连接，多个匹配实例时用 `--instance <实例ID>` 明确选择，也可在 profile 的 workspace 字段固定实例。每次调用绑定工作区和文档代次。

GUI 读取包括可序列化文件草稿；未应用表单字段、同文件 JSON/YAML 冲突和不可序列化草稿会返回诊断，并保留输入。提交可 Undo/Redo，saved 为 false；`workspace.save` 才明确保存。校验、运行和打包消费同一工作区版本的独立快照，不隐式保存。

磁盘在 GUI 外改变后，使用 `workspace.external.status` 查看新增、修改、删除及两边内容，`workspace.external.resolve` 按两份版本提交取舍或合并。reload 只允许无草稿；disk 明确放弃全部工作区草稿；workspace 保留工作区并写入磁盘；merge 提供每个外部变更路径以及每份未保存文件草稿的最终 UTF-8 内容或 null，有未应用字段时还需提供最终定义文件。完整结果可替换暂时无效的草稿。操作说明见 `describe`。编辑器“外部编辑与连接”面板提供相同入口，未完成的合并输入随文档保留。

写操作使用唯一 `--request`。同标识同参数可核对原回执，不重做导入；同标识不同参数报错。`request.result` 查询，磁盘部分写入通过 `request.recover` 显式续写。状态数值：0 Applied、1 Conflict、2 PartialFailure、3 Unknown。磁盘回执持续保留于 `.bgb-author`；删除它后无法确认历史结果。GUI 用 request.result 查询回执，request.recover 返回 recovery.unavailable。GUI 回执保留在当前 Editor 工作区生命周期内；应用退出后不能据未知回执自动重放。

退出码：0 成功；1 操作失败；2 参数错误；3 编辑冲突；4 部分失败或未知；130 取消。普通日志在 stderr，结构化结果在 stdout。取消写入可能返回部分失败，应核对请求结果。
