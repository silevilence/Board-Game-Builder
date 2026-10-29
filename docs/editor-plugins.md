# 编辑器插件开发

编辑器固定 CordiSharp / CordiSharp.Plugins.Avalonia 0.2.0、Avalonia 12.1.1。
`EditorHost` 只创建窗口、根 Context、启动内置插件和报告启动失败。
`EditorApplication` 检查 API、能力冲突、依赖顺序和停用策略；实例、服务、事件和资源
实际使用 CordiSharp 的 Inject、Provide、On、Effect 和 fiber 释放。
已移除旧 PluginHost/PluginContext，全部基础及可选功能使用 EditorPluginDefinition。

## 注册与依赖

注册一个稳定 ID、API=1、提供及依赖能力名和激活回调。提供者唯一；缺失依赖显示等待原因，
实现版本由 `EditorPluginDefinition.Version` 声明（默认 `1`），不得为空。
循环和 API 不兼容失败。Enable 激活依赖链，Disable 反向释放依赖者，Reload 正向恢复；
初始化失败撤销本次新增资源和注册，无关插件保留。必需插件只在应用退出时释放。
仅通过 `scope.Service<T>(声明过的能力)` 获取公开能力；服务使用 `scope.Provide` 发布。
事件使用 CordiSharp EventKey；用 `scope.On` 订阅，生命周期自动退订。

面板使用 `scope.Service<PanelContributions>("panels").Register(scope,id,title,factory)`。
工厂和资源清理在 UI 线程，返回现有 Control/UserControl 即可；ID/标题必须有效且 ID 唯一。
UI 通过 CordiSharp Avalonia 的 Slot 挂载，面板控件 IDisposable 随贡献注销释放。
不要存放跨重建状态在控件中，也不要将强引用存入静态字段。
作者格式支持在每个 `DocumentWorkspace.AuthorFormats` 目录中单独声明，以加载器 ID、实现版本和 API 精确匹配。插件使用 `scope.Own(documents.AuthorFormats.Register(provider))` 注册 `GameAuthorFormatProvider`，卸载时撤销支持并通知格式面板；不会影响另一编辑器工作区。
`IGameAuthorFormat.Read` 提供面板使用的作者配置模型，`Serialize` 负责写回该格式；文档修改／导入经此接口，不直接假定原始文件就是作者模型。没有 provider 的加载器继续使用通用文件编辑和试玩，`definition.state` 不自动获得结构化 UI。
草稿用真实路径和 `DocumentDraftTarget(path, Yaml:true)` 显式区分 JSON／YAML 视图；字符串路径没有 `definition` 或 `module.yaml` 别名，文件同名也不会被当成控制标记。
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
| sessions | base.sessions | ApplicationSessions，通用应用时钟、暂停／单步和两种试玩，必需 |
| viewports | base.viewports | 通用应用视口工厂；每个控件归所属面板所有，必需 |
| board.sessions | play.board.sessions | 桌游种子、席位、CPU、IDebugSession 和日志，可选 |
| board.viewports | board.viewports | 绑定席位的桌游视口，可选 |
| assemblies | base.assemblies | 受控 DLL 加载及真实回收诊断，必需 |
| exports / author.exports | author.export | 通用导出协调、提供者注册表、进度与取消，可选 |

可选功能：author.startup、author.files、author.resources、author.configuration、author.yaml、author.prototypes、author.media、
author.turns、author.layout、author.scripts、author.flow、play.controls、author.export。
资源树与检视器使用 documents.Select/SelectionChanged 同步选择，文档 Changed 同步内容。
桌游脚本和调试通过 board.sessions 服务，不调用主窗口私有函数。视图释放只取消自身订阅、原生输入和图形资源。旧扩展改用新能力键并重新构建；示例 Editor.SamplePlugin 已采用这一接口。

`author.export.package`／`author.export.player` 独立登记提供者与命令，参数来自共享 AuthorExportInfo；面板不按产物类型分支。所选方式与参数归文档 ViewState，重建面板不丢设置；缺少所选插件显示不可用。切换文档取消导出并忽略旧代次通知，强制关闭会等待执行退出后释放提供者。

实体、原型、媒介、席位及布局面板要求作者格式显式声明 `board.author-model` 能力，不能仅凭“支持结构化编辑”显示桌游模型面板。通用应用使用 play.application，面板启停不重启 sessions 中的模拟。

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

## 字段草稿与异步工作约束

席位/阶段、组件 YAML、媒介字段（包括尚不能解析的数值文本）通过 documents.Form/EditForm 保存纯文本，
和配置草稿一起纳入脏状态、取消离开及撤销/重做。字段草稿须先在对应面板“应用”或“放弃”，
保存和试玩遇到未应用字段时明确拒绝并列出名称。被删除实体留下的字段草稿可用全局“放弃所有字段草稿”移除。
表单应用检查作者格式接口指定定义文件的基线，其他面板已修改则拒绝并保留输入，要求重新读取并合并。

运行时 CordiSharp 核心与编辑器共享版本，但使用独立根和运行插件目录，不接受编辑器服务。游戏文件入口和格式见 [运行时插件](runtime-plugins.md)。`FormatPanel` 把格式专用控件放入子 fiber；文档切换后忽略不适用格式的旧通知，通用文件编辑、草稿及活动会话不归该子 fiber 所有。

初始化及 Own 资源释放通过可等待的异步 UI 派发；卸载会等待 UI 清理结束。
不要在 CordiSharp 调度线程上同步等待 UI，因为 UI 可能正在等待运行快照加载。
EditorApplication 保留原构造接口，EditorHost 传入 Avalonia 的异步派发实现。

内置 InitializeAsync 有 15 秒初始化边界；超时先取消 scope，再等待 fiber 清理。
迟到任务不能通过取消后的 scope 登记服务/事件/资源，Own 会立即释放迟到资源。
程序集扩展本阶段只支持及时返回的同步 IPlugin<EditorPluginScope> 入口，显式拒绝 IAsyncPlugin：
CordiSharp 0.2.0 内层异步入口无法由外层安全中止，不能把超时包装误作卸载保证。
后台工作须在同步入口注册 Effect，销毁时取消并等待；不允许永久阻塞初始化或清理。
可信本地 C# 不具备强制中止任意死循环的安全沙箱，异步入口扩展留待上游提供可终止边界后单独验证。

## 流程设计贡献

`author.flow`（实现版本 1，API 1）提供 `flow.author`，依赖 documents、board.sessions、panels、commands、workspace。
贡献“流程设计”面板及 flow.open/create/validate/play/stop/observe/resume 命令。
`FlowWorkspace` 通过共享 `IGameAuthorFormat`、`ProjectView` 和 `WorkspaceEditMode` 提交作者操作；
图、脚本与设计数据不归面板持有。属性和名称字段使用工作区表单，分别检查整个来源版本；
应用一个表单保留其他表单，基线冲突需要显式协调。干净表单缓存不影响作者 Undo 历史。

流程面板以独立格式谓词挂在 `FormatPanel` 子 fiber 中，不依赖主定义的作者支持。
谓词失效后释放控件及订阅，旧格式通知在访问文档前过滤。流程校验同步读取不可变项目视图，
没有会跨代次覆盖结果的后台校验任务；长时间试玩由 board.sessions 管理，命令执行期间 Guard
阻止插件停用，重建／停用面板不重启已有会话。运行层与 Player 不引用这些能力。

`ExportWorkflow` 通过工作区编辑模式读取一次来源，再物化 `ProjectSnapshot` 用于预检与正式导出。
它不隐式保存，也不清除工作区草稿；插件附加步骤仍有生命周期租约，临时快照清理失败明确报告。
