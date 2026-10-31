# 外部运行插件样例

`Factory.Create()` 返回通用运行插件：提供计数服务，每次 update 增加一次，不引用桌游状态或编辑器。插件安装与游戏启用是两个独立步骤。

```powershell
pwsh -NoProfile -File samples/Runtime.SamplePlugin/pack.ps1 -Output artifacts/runtime-counter-package
```

生成目录只包含实现 DLL 和带实际文件哈希的 `runtime.plugin.json`。共享 Application 与 CordiSharp 契约由宿主提供。此样例包尚未声明 AOT 支持；不能用普通加载测试替代原生验收。

普通宿主用 `PluginStore.Import` 显式导入；`Acquire` 返回的租约必须在会话完全关闭后才释放。工厂加入 `ApplicationCatalog`，在 `game.start.json` 中按清单选择精确 ID、版本、API，并把 `PluginManifest.Fingerprint` 写入 `implementation`。包内 DLL 不会因游戏文件被打开而自动导入。

运行 `dotnet run --project tests/Runtime.PluginImport.Tests -c Release` 验证实际加载、update、精确身份、活动版本保护和失败清理。后续 Editor 管理界面与普通独立发行另有 ROADMAP 任务。
