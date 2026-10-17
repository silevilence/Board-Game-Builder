# 随包字体

工程在 `game.start.json` 中显式启用通用内置资源插件：

```json
{"id":"resources.font","version":"1","api":1,"configuration":{"path":"assets/font.otf","license":"assets/OFL.txt"}}
```

该插件依赖 `resources`。一个工程选择一种桌面字体；支持静态 OpenType TTF 和 CFF OTF，不接受字体集合、WOFF 或可变字体。配置指向工程内文件，字体和非空许可证都纳入运行身份、快照与正式打包。缺件、越界字体表、格式不支持均返回文件位置。加载不安装字体，不查询系统字体。

使用现有 `files.write` 修改启动配置，`project.validate` 校验，`project.pack`／`player.export` 收集资源；不增加 CLI 命令或协议。也可以在编辑器通用文件草稿中编辑配置，并按现有保存流程处理。

MonoGame 视口直接从资源池加载字节，用 [SkiaSharp 字体接口](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.sktypeface?view=skiasharp-3.119)生成文字纹理；配置字体缺字时报告诊断，不悄悄换成系统字体。桌面动态文字、提示和日志均可使用这一字体。独立 Player 的操作控件使用同一文件，通过 [Avalonia FontManager](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Media/FontManager.cs) 的独立字体集合注册，关闭对局时注销。未配置字体的既有工程保持原显示方式。

资源池没有 UI 依赖；视口拥有字体对象与纹理缓存，游戏切换重配，释放视口时释放字体。每个 Player 游戏界面拥有唯一集合身份，关闭一个界面不会删除另一个界面的字体。不把作者机器字体目录或安装状态当作工程依赖。

验证入口：`resource-font`／`resource-plugins` 测试组，以及 `tests/View.Tests` 的真实字形、渲染、切换和独立释放检查。发布版 CLI／Player 的实际验收证据见各 sample 的使用记录。
