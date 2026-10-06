# 编辑器插件开发

编辑器固定 CordiSharp / CordiSharp.Plugins.Avalonia 0.2.0、Avalonia 12.1.1。
`EditorHost` 只创建窗口、根 Context、启动内置插件和报告启动失败。
`EditorApplication` 检查 API、能力冲突、依赖顺序和停用策略；实例、服务、事件和资源
实际使用 CordiSharp 的 Inject、Provide、On、Effect 和 fiber 释放。
已移除旧 PluginHost/PluginContext，全部基础及可选功能使用 EditorPluginDefinition。

## 注册与依赖

注册一个稳定 ID、API=1、提供及依赖能力名和激活回调。提供者唯一；缺失依赖显示等待原因，
循环和 API 不兼容失败。Enable 激活依赖链，Disable 反向释放依赖者，Reload 正向恢复；
初始化失败撤销本次新增资源和注册，无关插件保留。必需插件只在应用退出时释放。
仅通过 `scope.Service<T>(声明过的能力)` 获取公开能力；服务使用 `scope.Provide` 发布。
事件使用 CordiSharp EventKey；用 `scope.On` 订阅，生命周期自动退订。

面板使用 `scope.Service<PanelContributions>("panels").Register(scope,id,title,factory)`。
工厂和资源清理在 UI 线程，返回现有 Control/UserControl 即可；ID/标题必须有效且 ID 唯一。
UI 通过 CordiSharp Avalonia 的 Slot 挂载，面板控件 IDisposable 随贡献注销释放。
不要存放跨重建状态在控件中，也不要将强引用存入静态字段。
命令通过 `EditorCommands.Register` 贡献，菜单、工具栏、快捷键共用状态及执行体；
TextBox 和原生视口有各自焦点边界，作者命令不得误触发。
导入动作注册为命令；导出附加步骤为 `EditorContributions.Register(scope,"export-step",id,Action<ExportContext>)`，
运行时取得所有者租约，执行中不能释放。附加步骤失败不发布最终目录。

## 状态归属

| 能力 | 提供者 | 生命周期 |
| --- | --- | --- |
| workspace / panels | base.workspace | 工作区及 Slot，必需 |
| commands / contributions | base.commands | 命令、菜单和附加步骤目录，必需 |
| documents | base.documents | 文档、配置草稿、Undo/Redo、选择、未保存协调，必需 |
| sessions | base.sessions | 种子、席位、CPU 调度、两种 PlaySession 和日志，必需 |
| viewports | base.viewports | 共用视口工厂；每个控件归所属面板所有，必需 |
| assemblies | base.assemblies | 受控 DLL 加载及真实回收诊断，必需 |
| exports | author.export | 导出进度、目录、步骤执行保护，可选 |

可选功能：author.resources、author.configuration、author.yaml、author.prototypes、author.media、
author.turns、author.layout、author.scripts、play.controls、author.export。
资源树与检视器使用 documents.Select/SelectionChanged 同步选择，文档 Changed 同步内容。
脚本和调试通过 sessions 服务，不调用主窗口私有函数。视图释放只取消自身订阅、原生输入和图形资源。

任何活动文档、Undo、会话或长操作持有扩展定义的 CLR 对象时，持有者必须取得
`app.Retain(pluginId, "可读原因")` 并在最后引用释放时 Dispose；也可由插件 `scope.Guard` 注册策略。
核心文档目前只存字节、字符串和内建类型，因此内置作者功能面板可重建而不丢历史。
扩展异步工作必须注册 Effect，在销毁时取消且等待结束；不能只移除标签页。

## 预编译示例及程序集边界

`samples/Editor.SamplePlugin` 是完整源码、清单和构建项目。维护者预编译：
`dotnet build samples/Editor.SamplePlugin -c Release`；作者在管理页加载生成的 DLL 即可，宿主无需修改。
同名 `.plugin.json` 声明 id/api/entry/provides/requires；入口实现 CordiSharp IPlugin<EditorPluginScope>，
Load 中用 `configuration.ForContext(ctx)` 将面板和服务绑定到真实程序集插件 fiber。
该路径只加载可信本地代码，不是 WASM 或不可信代码沙箱。

CordiSharp、Editor/Core、Avalonia 合约从默认加载上下文共享。示例使用共享控件类型，
不在可回收 DLL 定义 Avalonia Control 子类或注册全局 StyledProperty，以免 UI 类型缓存长期持有它们。
停用只注销贡献；重载只重建实例；程序集卸载先检查保留租约、清理 fiber 和模板/事件/任务，
再请求 collectible AssemblyLoadContext 卸载，等待 UI 延迟清理并用 WeakReference 验证。
若仍有强引用，报告“功能已注销，但程序集回收未通过”；不得把标签消失当成程序集回收成功。

验证入口：Editor.exe --verify-foundation <结果.json>；
Editor.exe --verify-assembly <示例.dll> <结果.json>。
真实加载/卸载、故意保留引用、图形设备和原生窗口清理证据见 M2.5 接入记录。
