# M1 Windows 视口

Player 独立进程与 Avalonia Editor 最小壳共用 `DesktopViewport` 和 `TableRenderer`。
WindowsDX GraphicsDevice 直接向 NativeControlHost 创建的子 HWND 呈现；不复制截图充当渲染。
MonoGame 3.8.4.1、Avalonia 11.3.10，Windows x64；没有替换既定渲染器。

左键选择，右键拖动相机，滚轮缩放。HWND 接收输入并转换到世界坐标；失焦取消拖动。
33ms UI 定时器绘制，尺寸变化重建设备。宿主关闭／重建显式 Dispose，释放纹理、SpriteBatch、设备及定时器；系统销毁 HWND 时恢复窗口过程。

运行 `Player viewport` 或 `Editor` 查看测试桌面。两者支持 `--smoke <结果.json>`，
执行三轮实际窗口绘制、尺寸变化及视口销毁，检查绘制帧和 LiveDevices 回到零。
证据在 `artifacts/m1-validation/*-viewport.json`；截图 `editor-probe.png`。
首次验证发现缺少 Windows supportedOS manifest 与延迟 ContentPresenter 卸载导致检查时资源仍在，均已修复。

限制：仅 Windows HWND 集成；原生视口存在 airspace 限制，Avalonia 控件放在视口外，不能覆盖原生区域。
字体使用 Windows Microsoft YaHei UI；初始相机采用宿主 DPI 缩放，后续缩放由视口相机控制。
设备创建失败停止渲染并提供错误；不自动切换另一种渲染器。
Player 与测试视口提供显式缩放按钮，已通过后台实际操作验证；当前自动化滚轮未观察到缩放，物理滚轮需人工复核。

参考：[Avalonia NativeControlHost](https://docs.avaloniaui.net/docs/platform-specific-guides/windows/)、
[MonoGame PresentationParameters](https://docs.monogame.net/api/Microsoft.Xna.Framework.Graphics.PresentationParameters.html)。
