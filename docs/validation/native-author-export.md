# NativeAOT 作者导出提供者验收

独立身份为 `author.export.native` / `native-directory`，操作为 `native.export`。注册沿用 `AuthorHost` 和 `AuthorExports`，没有增加独立执行框架。它从指定版本的 `ProjectView` 及已获准的应用作者目录加载内容；图形元数据从已验证 SDK 读取，不在作者进程打开设备，也不执行从游戏内容指定的任意 DLL。

后端必须由 `game.start.json` 显式选择，发布参数只有工具位置、目标和宿主配置。预检依次验证独立目标、SDK 文件身份、插件闭包、宿主能力与工具链。实际编译继续经过严格 NativeBuildService，不把兼容声明当作编译成功。作者程序集、运行库分发工具和发布服务不进入游戏引用。

## 2026-10-31 验证

```powershell
dotnet run --project tests/Authoring.Native.Tests -c Release -- artifacts/native-aot/sdk-motion-02 --native
dotnet run --project tests/Engine.Tests -c Release -- author-export-contract author-profiles author-exports
```

SDK 的维护者生成方式见[专用入口](native-game-entry.md)。实际测试通过：

- 三个提供者同时注册；停用、重新注册 NativeAOT 不影响其他两个提供者，停用后普通数据包实际导出成功。
- 缺少后端拒绝，不自动补 GL；缺 SDK 可执行文件返回 `toolchain.sdk`。
- 真实编译到 `Generating native code` 时尝试移除提供者，得到 `export.active`；随后取消，子进程退出，最终目录不存在，活动引用归零。
- 取消后同一提供者再次构建成功。产物使用代次 3 的内存草稿标题“未保存原生游戏”，回执绑定该 ProjectSource，原始 motion.json 与入口文件未修改。
- CLI 不启动 Editor，能够 `describe native.export` 并通过 `call native.export --args-file ...` 生成真实原生目录，回执包含产物类型、内容身份、源版本和构建日志。
- 普通导出契约、作者 profile、作者导出测试及边界检查通过。

证据位于 [evidence/native-author-export](evidence/native-author-export/)。Editor 面板、跨文档结果隔离及完整桌游／外部组合在各自后续任务验收。
