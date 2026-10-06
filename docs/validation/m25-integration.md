# M2.5 接入验证

实施基线：`bc73d0a35e1640aedd885e66ff7f34c3e1af2159`。日期：2026-10-06。

## 版本与接入选择

用户确认直接升级 Avalonia，目标为 12.1.1，与 CordiSharp.Plugins.Avalonia 0.2.0 对齐。
CordiSharp 核心固定 0.2.0；NuGet 包元数据记录源码提交
`7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f`。
上游参考：[CordiSharp](https://github.com/oniemiddle/CordiSharp)。
本地查阅的上游 HEAD 为 `01f121728be1455101e260d69ffcc27c8599e3fa`，不将 HEAD 等同于发布包源码。

项目原 Avalonia 11.3.10 无法直接满足扩展对 12.1.1 的依赖。
保留 11.3.10 加薄 UI 适配曾作为候选，用户明确选择升级后不再沿该路径实施。
MonoGame 保持 3.8.4.1，Windows NativeControlHost 与原生视口继续复用。

SDK 固定 10.0.112，其 Roslyn 为 5.0；CordiSharp 0.2.0 附带分析器／生成器引用 Roslyn 5.6，
导致 CS9057。通过 Directory.Build.targets 精确排除这两个可选构建工具；
不降级诊断等级，不屏蔽其他分析器，插件使用显式注册／注入及运行时反射。

## 可重复验证入口

`tests/Editor.Compatibility` 是真实 Avalonia 窗口与 MonoGame 图形设备探针。
运行参数为输出 JSON 路径；包含两个有依赖的插件、三轮启停、加载失败清理、
缺失依赖等待状态以及图形设备计数。探针与正式入口使用同一 Windows 应用清单。

```powershell
dotnet restore tests/Editor.Compatibility --locked-mode
dotnet build tests/Editor.Compatibility -c Release --no-restore
./tests/Editor.Compatibility/bin/Release/net10.0-windows/Editor.Compatibility.exe artifacts/m25/compatibility.json
```

## 接入结果

升级后的 Release 全解决方案构建通过，0 警告、0 错误。
将 9 处弃用的 TextBox.Watermark 替换为 PlaceholderText，未屏蔽弃用诊断。
锁定模式还原通过。`artifacts/m25/compatibility-v12.json`：3 轮、15 个实际绘制帧、
结束时 0 个图形设备、空失败列表；包含 UI 扩展槽位注销与加载失败回滚。
正式 Editor 的 `--smoke` 探针另通过三轮视口重建和长标签纹理复用检查。
面板工厂在 RegisterSlot 前执行，使用户工厂失败不会在上游槽位目录留下半注册项。

结论：采用 Avalonia 12.1.1 + CordiSharp / CordiSharp.Plugins.Avalonia 0.2.0。
无需替换渲染管线，可进入正式编辑器迁移。后续完整工作流及发行物回归单独记录。


## 基础宿主验证

`Editor.exe --verify-foundation artifacts/m25/foundation.json` 通过：基础插件打开空模块；
可选插件初始化失败后文档实例保持不变；必需文档插件停用被明确拒绝；退出后根 fiber 副作用数为零。
正常入口已使用 EditorHost 装配基础插件。文档、草稿、会话与各功能的进一步迁移按后续任务推进。

## 依赖策略验证

`dotnet run --project tests/Engine.Tests -c Release -- cordi` 通过。
A→B→C 依赖链按反向释放、正向恢复；B 初始化失败后本次链全部回滚，独立插件保留。
三轮重载后事件仅触发一次；缺失能力显示 Waiting 及缺失名称，循环、重复能力、API 不兼容明确失败。
活动引用 guard 在任何销毁发生前检查，拒绝停用后仍保留实例；引用解除后可停用。

## 面板贡献验证

`artifacts/m25/panels.json` 通过：独立面板三轮启停、重复 ID 拒绝、第二个工厂失败时
撤销该插件已生成的第一个面板，基础设置和插件管理面板仍在。
工厂、挂载、移除与 IDisposable 清理均在 UI 线程执行，清理令牌幂等。

## 命令验证

`artifacts/m25/commands.json`：统一命令在真实按钮及 Avalonia Ctrl+F8 路由上每轮各执行一次，
禁用后按钮与注册器均拒绝执行；三轮启停无重复，注销后注册器为空。
菜单与工具栏共享 ICommand；作者快捷键避开 TextBox 和试玩视口焦点。
服务及事件由 CordiSharp 提供，导入动作使用命令接口，附加步骤使用有归属及执行租约的贡献目录。

## 文档与草稿验证

`artifacts/m25/documents.json` 通过真实配置面板重建后草稿保留、草稿 Undo/Redo、
取消离开保留状态、多入口冲突保留草稿、已应用脚本修改的 Undo/Redo 以及保存重开指纹一致。
基础文档插件拥有 ModuleDocument、草稿、Undo 与未保存选择；退出检查注册到应用关闭协调器。
JSON 与 YAML 对同一文件的并行草稿明确拒绝同时应用，不静默覆盖。

## 作者面板验证

`artifacts/m25/author-panels.json` 通过：资源树、YAML、原型、媒介、席位面板分别重启；
创建原型、实例化、更新组件后保存重开可加载，公共选择状态在面板重建后保留。
核心 documents/prototypes/media/layouts/medium 回归通过。字段应用新增并发冲突拒绝。
旧窗口及集中工作区装配已移除；完整工作流驱动将在新会话面板接入时迁移至新宿主。

## 会话状态归属验证

`artifacts/m25/sessions-stop.json`：进程内与独立 Player 均完成三轮试玩面板重建，
同一会话对象和命令序号保持；忙时暂停、停止后旧异步结果忽略均通过。
两模式各 7 条命令终局及回放一致；停止后无帧、日志、忙碌状态残留，退出设备数为零。
SessionController 由必需基础插件管理 CPU 调度与会话生命周期，PlayPanel.Dispose 仅释放显示资源。

## Lua 插件验证

`artifacts/m25/scripts.json`：两种 Play 模式分别重载规则和 CPU，语法错误保留原状态及命令序号；
脚本面板重启不丢失文档文本，日志包含至少三版脚本并能重放到相同终局。
停止和面板停用后的异步结果通过会话代际及面板取消令牌隔离。既有 reload 回归通过。

## 视口生命周期验证

`artifacts/m25/viewports.json` 通过：布局插件三轮创建、窗口调整尺寸、缩放、输入取消及销毁；
每轮图形设备和原生子窗口计数均回到 0。编辑与试玩从同一视口服务创建 InteractiveTable。
补齐失焦／分离时释放自身鼠标捕获、取消输入，以及渲染缩放变化时更新相机并取消旧手势。
freecell-shortcuts、layouts、interaction、view 回归通过，空当接龙合法通关快捷操作保持有效。

## 调试面板验证

`artifacts/m25/debug-panels.json`：两种模式按各席位观察时不返回权威状态，显式全信息观察才返回；
关闭全信息后恢复普通席位投影。面板重建仍显示同一暂停会话，非法动作、CPU 单步与回放通过。
observer/timeline/debug-play/debug-external 回归通过；控制器同步当前观察身份与显式全信息标记。

## 导出工作流验证

`artifacts/m25/export-cards-verified/result.json` 通过：实际附加步骤执行、启停注销、
运行中导出及步骤所有者拒绝停用、缺失发行物诊断、目标目录保持不变。
修复 Avalonia 文本框延迟初始化事件覆盖目标目录的问题。
正式自包含 Player 重新发布为 Avalonia 12.1.1，导出核心测试通过；
`artifacts/m25/export-smoke.json` 从本次导出完成两局 GUI 游戏，无编辑器依赖。
预检验证 PE WindowsGui 子系统，正式启动不使用控制台入口。
