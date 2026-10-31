# Editor 外部运行插件管理验收

2026-10-31，Windows x64。`author.runtime.management` 提供独立的运行插件管理面板与公共命令，复用 `PluginStore` 的安装、校验和活动版本保护。Editor 扩展仍使用原插件管理。

验证入口：`Editor.exe --verify-runtime-management <external-delivery-fixture> <new-output>`。fixture 由 `tests/Runtime.PluginDelivery.Tests` 生成；设置独立 `BGB_RUNTIME_PLUGIN_STORE`，不修改用户安装目录。

- 三个外部包的显式导入不会启用工程；先导入缺依赖的视图包会显示缺失能力，目录保持原状。
- 启用写入精确实现指纹，支持工作区撤销／重做；AOT 未声明显示为不能发布，不提供覆盖声明的勾选框。
- 重复版本必须显式替换，错误平台和 Editor 类型包被拒绝。空闲替换后原工程指纹保留，并显示不匹配诊断。
- 活动试玩期间移除和替换均被拒绝；取消并等待会话退出后可移除，工程选择继续保留并显示缺失实现。
- 管理插件反复停用／启用不重复注册命令，也不丢安装目录或工程选择。
- 导入单测 26 项通过，增加固定宿主能力依赖、固定身份冲突与取消导入；普通外部发行回归通过，项目边界检查通过。

通过 Windows UI 实际点击导入 `sample.counter`，看到只读 AOT 声明与完整指纹；再启用并撤销。工程快照从 `F60767182E4B` 变为 `48D7F22657F1`，撤销后恢复原值，未保存标记消失；原样例磁盘未改变。

报告、自动验证画面和实际点击画面保存在 [evidence/editor-runtime-management](evidence/editor-runtime-management/)。本项不宣称完成后端切换或 NativeAOT 联合验收，后续条目单独验证。
