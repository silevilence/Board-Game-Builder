# DesktopGL 正式后端集成

2026-10-31，Windows x64，SDK 10.0.112 / NativeAOT 10.0.12。`Rendering.DesktopGL` 实现正式 `IRenderBackend` / `IRenderSurface` / `IDraw2D`，可由 `DesktopGlBackend.Create` 静态登记。经验证的身份为 `render.desktopgl` 版本 1、平台 `windows-x64`、支持 NativeAOT；WindowsDX 的普通运行与不支持 AOT 声明保持不变。

平台视口继续拥有 HWND、输入、DPI、焦点与取消。GL 使用其子窗口提供设备，在每次创建／绘制／释放资源前显式切换 context；一个图形宿主共享一个后端，每个视口独立持有设备和缓存。SDL 不运行第二套 Windows 消息循环。后端仅处理资源与绘图，不识别规则或动作。

`BoardGame.Avalonia.InteractiveBoard` 接受显式图形配置，复用桌游绘制与交互；原 `View.MonoGame.InteractiveTable` 保留 WindowsDX 默认入口兼容调用者。GL 不依赖该 WindowsDX 程序集或 Editor。Editor 的工程后端选择和安全重建另有验收项，本项提供其共享视口实现。

## 依赖与复现

MonoGame 3.8.4.1 使用 [固定源码补丁](../../third_party/MonoGame/README.md)，构建脚本校验源码和补丁身份。后端另外依赖 SDL 2.32.2.1、SkiaSharp 3.119.4；Windows x64 运行文件为 `SDL2.dll`、`libSkiaSharp.dll`。Avalonia 宿主自身还带 `av_libglesv2.dll`、`libHarfBuzzSharp.dll`。字体由游戏资源提供，许可随资源携带；不能凭宿主电脑安装的字体宣称中文发行成功。

```powershell
pwsh -NoProfile -File third_party/MonoGame/build-patched.ps1
pwsh -NoProfile -File third_party/CordiSharp/build-static.ps1
pwsh -NoProfile -File tests/Rendering.Games.Tests/verify.ps1 -Native
dotnet run --project tests/Editor.Compatibility -- .scratch/legacy-compatibility.json
pwsh -NoProfile -File scripts/check-boundaries.ps1
```

## 验收结果

- 双视口共享后端、独立设备；三轮缩放／尺寸变化／截图／设备重建；原生鼠标、键盘、失焦取消；初始化取消、设备失败及恢复均通过，最终设备／后端／原生窗口计数为零。截图包含中文、图片、图集旋转与裁剪。
- 相同游戏分别使用 GL 和 WindowsDX：实际原生窗口按键使实时样例到达目标、重置，暂停时 render 不推进 update；实际窗口点击完成桌游 shuffle→draw→finish，再校验回放。尺寸及缩放改变后交互继续正常。
- GL / WindowsDX / 原生 GL 状态 hash 均为 `C4A0FC1983F2D9895B910EFCB4AD6140F0E0FE7C4DEE6B2B557398C5C4ABC379`，记录 hash 均为 `027C97CE7BE970C13D2E3777077C4F0F6357A790C1D3AC72CE4E7582E1FF4634`。本机两个后端的桌游 PNG 逐字节相同。
- 原生游戏目录复制到无系统 .NET、源码及作者工具的 Windows Sandbox 10.0.26100，实际交互、对局、回放与关闭通过，退出 0，哈希与本机一致。
- 旧 CordiSharp / WindowsDX 视口兼容测试完成三轮挂载、卸载和失败清理，设备为零，无失败。

报告存放在 [evidence/desktopgl-integration](evidence/desktopgl-integration/)。原生编译使用严格 ILLink / ILC 警告处理，没有全局关闭分析。此处认证的是被正式后端消费的 API，不扩展为所有 MonoGame 或 SkiaSharp API 均支持 NativeAOT。
