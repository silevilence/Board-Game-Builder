# 原生发布、GL 与外部运行插件

工具包的 Editor、普通 Player 和独立调试 Player 均附带可选 DesktopGL 后端，默认 WindowsDX 仍可用。GL 要求目标图形会话支持 MonoGame 必需的 OpenGL framebuffer 扩展；只有桌面连接成功并不代表驱动满足要求。

原生导出是独立的作者插件 `author.export.native`。CLI 和 Editor 各自附带匹配的预编译 `native-sdk`，一般无需填写 `sdk` 路径；普通自包含导出 `author.export.player` 和数据包导出 `author.export.package` 不依赖原生工具链。不要混用其他版本的 SDK 或手工替换其中 DLL。

原生构建机需要 .NET SDK 10.0.112、x64 Visual Studio C++ 工具链和 Windows SDK，发行运行库固定为 10.0.12。玩家只需要 Windows x64、支持所选后端的图形驱动及完整游戏目录，不需要 SDK、系统 .NET 或 Editor。当前支持无脚本桌游、固定整数 ABI 的 WASM、经验证的外部 .NET 插件，以及实时应用。Lua 规则、CPU 和流程暂不支持原生发行；继续使用普通 Player 导出，不会自动降级。

在作者 profile 中启用 `author.export.native`；无脚本桌游还使用 `author.runtime.board-application`，校验使用 `author.validation`。运行入口必须显式选择一个后端，例如 `render.desktopgl`；桌游还须声明 `board.render`、`board.visual-input` 和 `platform.viewport`。

先查询当前安装的参数，再执行导出：

```powershell
bgb.exe describe native.export --project C:/Games/MyGame
bgb.exe call native.export --project C:/Games/MyGame --args-file native.json
```

`native.json` 示例：

```json
{"target":"C:/Exports/MyGame","host":"board.desktop","platform":"win-x64"}
```

输出目录必须不存在。实时应用使用 `application.desktop`。可选参数包括 `title`、`version`、工程内 `icon`、自定义 `sdk` 以及工具链路径。Editor 同一导出方式提供预检、进度、取消和日志位置；导出使用捕获的草稿版本，不隐式保存工程。失败或取消不会发布成功目录。

外部 .NET 插件先通过运行插件管理面板或 `runtime.plugins.import` 显式导入，再在游戏入口选择精确版本和实现身份。普通发行携带所选包，原生发行在构建时静态装配它们；游戏文件不能指定任意 DLL 路径来安装代码。兼容声明只用于准入，不能代替传递依赖检查、严格编译和实际运行测试。正在试玩或导出的实现不能被移除或替换。
