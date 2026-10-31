# Editor 运行插件配置读写验收

`author.startup` 贡献配置服务与面板；`runtime.catalog` 聚合已启用作者运行能力的元数据，以及显式导入包的声明。元数据目录不激活游戏插件。应用和桌游会话仍分别属于 `sessions` / `board.sessions`。

## 2026-10-31，Windows x64

验证入口：

```text
Editor.exe --verify-runtime-configuration <external-delivery-fixture> <new-output>
Editor.exe --verify-runtime-documents <result.json>
Editor.exe --verify-export samples/quick-cards <new-output> <current-player-release>
```

外部 fixture 由 `tests/Runtime.PluginDelivery.Tests` 产生。为配置测试设置独立 `BGB_RUNTIME_PLUGIN_STORE`，所有另存为和导出写入新测试目录。

配置测试覆盖：

- 非桌游工程只保留其显式组合；可查看内置、外部和图形元数据、作用域、依赖、回调阶段及顺序约束。
- 暂时无效 JSON、未知配置与缺失实现均保留；重载面板不丢草稿，撤销恢复原文。
- 从已安装实现选择外部版本会写入精确指纹；启用、移除、撤销和重做进入同一文件草稿，校验消费该版本。
- 启动后修改配置不会替换会话，面板继续显示会话原快照和身份。
- `author.publish.json` 参数可撤销、保存、重开；普通导出实际消费重新读取的目标。停用提供者仍保留其参数，包括未知提供者／参数。
- 外部定义没有结构化作者格式时仍可编辑普通文件、校验、另存为和重开。切到其他文档后不串用先前发布配置，原样例磁盘文件不变。
- 旧运行文档验证通过：格式子上下文、原有 JSON/YAML 草稿、普通文件编辑、桌游试玩和反复启停无回归。

普通导出回归使用当前重建的 Player 基包。旧本地基包未声明 `board.render`，被预检正确拒绝；没有修改或放宽身份规则以接受它。

报告及界面位于 [evidence/editor-runtime-configuration](evidence/editor-runtime-configuration/)。本条仅完成配置读写；运行插件管理 UI、后端实际切换及 NativeAOT 面板联合验收另按 ROADMAP 执行。
