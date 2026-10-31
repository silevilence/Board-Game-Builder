# MonoGame DesktopGL 原生嵌入与多视口实证

日期：2026-10-31。对应 ROADMAP 的 GL 图形宿主原型与后端适配前置实证。用户已允许保留 MonoGame 并维护必要后端源码补丁。独立窗口和嵌入探针均已有严格 AOT 构建及干净 Windows 运行证据；正式 Editor／Player 后端、真实游戏与联合验收仍未完成。

## 方案及源码身份

以 MonoGame.Framework.DesktopGL 3.8.4.1 的固定源码提交 `f34200720b558125964273fd3e7ab44cde0b0429` 为基础，每个视口创建自己的 SDL OpenGL 窗口，把 SDL 提供的 HWND 作为 Win32 宿主子窗口。SDL 创建和拥有窗口的像素格式；宿主通过显式图形线程在设备间切换 context。没有把普通 HWND 假装成 `SDL_Window*`，没有复制游戏规则或以帧回读取代视口绘制。

补丁和获取／构建方式位于 [third_party/MonoGame](../../third_party/MonoGame/README.md)。补丁 SHA-256：`EFD828319E991EA2F27E4CB9C005F2A294744407E3ABBF2AC1DF8EDD81600BB6`。完整上游源码只放在忽略的 `.scratch`，仓库保留补丁和 MS-PL 许可证。

补丁范围：

- `GraphicsDevice` 的 context 创建和重新激活使用显式 SDL 窗口；提供 `MakeCurrent`，要求创建、绘制、读回和释放均在图形线程。
- `GraphicsAdapter.FromSdlWindow` 将显示模式查询绑定视口窗口，减少对 `SdlGameWindow.Instance` 的依赖。
- 原先静态的顶点属性／buffer 绑定缓存改为设备字段，避免切换独立 GL context 时沿用另一设备缓存。
- 设备释放前先激活自己的 context；SDL context 及时释放，构造失败清理 context；`IsCurrent` 查询真实 SDL 状态。
- 标准 `Game` 的 `GraphicsDeviceManager` 在 SDL 重建窗口后更新 `DeviceWindowHandle`，保留独立游戏入口的装配方式。另用链接到原始 GL 探针源码的 `GameRegression` 工程验证此分支。

## 复现

在仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/AotProbes/GlEmbedded/run.ps1
```

标准 `Game` 路径的回归工程复用 `tools/AotProbes/Gl/Program.cs`，但引用同一补丁 DLL：

```powershell
dotnet publish tools/AotProbes/GlEmbedded/GameRegression/GameRegression.csproj -c Release -r win-x64 -o artifacts/native-aot/gl-embedded/game-publish
# 从原生产物目录启动 AotProbe.GlGameRegression.exe，参数为独立的证据目录。
```

脚本获取精确源码和两个图像解码子模块、核验补丁、构建补丁程序集，然后以 Windows x64 严格 `PublishAot` 发布并运行探针。已有源码含其他修改时拒绝继续。上游源码以 `net8.0` 构建，探针使用仓库 SDK 10.0.112、`net10.0` 与 ILCompiler 10.0.12。

| 内容 | 位置 |
| --- | --- |
| 探针和 P/Invoke 源码 | `tools/AotProbes/GlEmbedded/` |
| 补丁库身份、DLL 哈希及完整源码分析日志 | `artifacts/native-aot/monogame/build-manifest.json`、`source-build.log` |
| 严格 AOT 发布日志／退出码 | `artifacts/native-aot/gl-embedded/build.log`、`build-exit.txt` |
| 原生可运行目录 | `artifacts/native-aot/gl-embedded/publish/` |
| 本机结果、退出码及 stderr | `artifacts/native-aot/gl-embedded/host/result.txt`、`exit-code.txt`、`stderr.log` |
| 双视口及单视口存活截图 | `host/pass-<0..9>-a.png`、`-b.png`、`-survivor.png` |
| 补丁库的标准 Game 回归 | `game-build.log`、`game-publish/`、`game-host/` |

完整产物位于忽略的 artifacts，可运行脚本重新生成。精简干净机证据已归档到 [evidence/native-aot-gl](evidence/native-aot-gl/README.md)，包含环境、结果、退出码、运行输入哈希及一张可检查的截图；不能仅凭本文复用“通过”状态到其他版本或机器。

三条验证路径分别回答不同问题：

| 路径 | MonoGame 来源 | 本机 | 干净 Windows | 证明范围 |
| --- | --- | --- | --- | --- |
| `tools/AotProbes/Gl` | 原始 DesktopGL 3.8.4.1 NuGet 包 | 严格 AOT 与三轮运行通过 | OS 26100，退出 0 | 原始库的独立 `Game`、中文图片、resize／dispose |
| `GlEmbedded/GameRegression` | 同版本显式 context 补丁 DLL；链接原始 `Gl/Program.cs` | 严格 AOT 与三轮运行通过 | 未单独运行 | 补丁没有破坏标准 `Game` 窗口创建路径 |
| `tools/AotProbes/GlEmbedded` | 同版本补丁 DLL | 严格 AOT 与十轮运行通过 | OS 26100，退出 0 | 同一 Win32 宿主中的两个 SDL 子窗口、context 切换与释放 |

独立 `Game` 的成功不证明 Editor 嵌入；Win32 双子窗口的成功也不证明 Avalonia 消息泵、焦点与多文档已接通。

## 本机结果与边界

环境为 Windows `10.0.26200` x64、.NET SDK 10.0.112。严格 AOT 发布成功，日志未降低 warnings-as-errors、未增加 root descriptor，也未抑制 AOT／裁剪诊断；执行程序报告 `RuntimeFeature.IsDynamicCodeSupported=False`。补丁库的全 API 源码构建仍有上游 36 条警告，包含未进入该二维探针的 ContentManager、设计转换器和 STB 分析警告，完整保留于 source-build.log；严格发布结论只覆盖探针实际可达路径，不能推广为整个 MonoGame API 认证。

探针验证十轮窗口／设备创建释放，每轮两个并存的 SDL OpenGL 子 HWND，交替 48 帧绘制各自背景、PNG 图片与随包 Noto CJK 字体。每帧从真实 backbuffer 检查像素，截图来自实际渲染结果。原型截图已人工查看，中文和图片可见；这是验证用读回，正式渲染路径没有以读回图像嵌入控件。

同一补丁 DLL 的标准 `Game` 回归也通过严格 AOT 发布，并在本机完成三轮创建、绘制、调整尺寸与释放，进程退出码 0；没有更改原 GL 探针源码。该回归覆盖补丁涉及的默认窗口 handle 更新，不替代真实游戏功能回归。

每轮将两个视口调整到不同尺寸，并验证先关闭一个视口后另一个仍能绘制；WM_LBUTTONDOWN／UP 消息分别经 SDL 进入目标 window ID，说明基础输入路由没有被另一个视口消费。另验证非图形线程调用被拒绝、没有 OpenGL 标记的 SDL 窗口无法创建 context，失败后没有 current context 遗留。销毁时检查子 HWND 已不存在，全部关闭后 current GL context 为零、显式 surface 所有权计数归零。多次十轮运行中 GDI 资源预热后为 57，USER 资源约 48～52 波动，没有逐轮递增；这是该运行区间的有界观察，不是所有驱动、窗口情形的泄漏证明。

原型曾遇到调整大小后的黑边：只有 Win32 子窗口样式／尺寸变化并不足以更新 SDL drawable，SDL 窗口还须声明 `Resizable`，并在宿主尺寸更新后泵送事件、核对 drawable 尺寸再更新 backbuffer 和 viewport。当前探针显式保留此检查，尺寸不一致时失败，不把黑边图像当成功。

## 干净 Windows 结果

主代理在新 Windows Sandbox 执行同一嵌入 exe，目标 OS 为 `10.0.26100`，检查结果为 `dotnet=false`、`runtime=false`、`runtimeX86=false`。Sandbox 只映射只读探针发行目录和可写证据目录，没有挂载源码或作者安装；网络关闭。嵌入原生 exe 的 SHA-256 为 `D10F9AB8496A73CE472EAACE408F52FE52731200134A814867E5C001A0ACDF27`，与本机探针一致。

嵌入进程退出码 0，`dynamicCodeSupported=False`，十轮全部通过，每轮关闭后的 GDI=13、USER=9，完全稳定。无 OpenGL 窗口拒绝、图形线程拒绝、消息路由、resize、单视口存活和 HWND/context 清理均来自同一探针结果。[环境](evidence/native-aot-gl/environment.json)、[结果](evidence/native-aot-gl/embedded-result.txt)、[退出码](evidence/native-aot-gl/embedded-exit.txt)、[哈希](evidence/native-aot-gl/hashes.json)

原始 NuGet 版独立 `Game` 探针在同一干净环境也完成三轮并退出 0，运行时动态代码支持为 false。[独立探针结果](evidence/native-aot-gl/standalone-result.txt)、[运行时](evidence/native-aot-gl/standalone-runtime.txt)

归档截图是最后一轮关闭第一个视口后，第二个视口的实际 backbuffer；已查看中文和图片，截图不是完整桌面或物理输入证据。[代表截图](evidence/native-aot-gl/embedded-survivor.png)

## 正式接入仍须完成

1. 将 SDL 视频子系统放到图形宿主所有权中，平台视口持有 SDL 窗口与 surface，最后一个视口退出后再释放宿主；所有操作使用同一图形线程。
2. Avalonia `NativeControlHost` 应挂载 SDL 提供的 HWND，并按其 native control 生命周期创建／释放。不要继续创建 `STATIC` HWND 后直接传给 DesktopGL。父 HWND 只用于挂载，`DeviceWindowHandle` 仍是 SDL 指针。
3. 将输入与 DPI／坐标变换接回既有通用契约，验证真实键鼠、焦点、拖动／捕获、多个文档、不同 DPI、取消和窗口关闭。当前只验证了注入 HWND 鼠标消息；未把物理输入或跨屏 DPI 标为通过。
4. 回归独立 `Game` 路径及正式 `IDraw2D` 图片、图集、缩放、中文、capture，验证 WindowsDX 与 GL 互斥选择。当前仅证明补丁平台接入，没有修改正式 `src`。
5. 原型已在干净 Windows 通过；正式后端与真实游戏仍须复跑相同隔离验收并记录 GPU／驱动、物理输入和 DPI，不能以原型替代正式产物。

上游 GL 入口和 function-pointer 表仍属于全局平台状态，此轮限定单图形线程及同一主机 GL 实现，未承诺跨 GPU／驱动同时创建 context。后续若扩大边界，应单独核查这些静态入口与能力缓存，不能只增加新平台标识。

## Avalonia 接口与消息泵研究（尚未实现验收）

当前契约 `IRenderBackend.CreateViewport(ViewportTarget)` 已允许后端在 `target.Handle` 下拥有子窗口。最小接入可保留 `PlatformViewport` 的 `STATIC` 输入宿主：GL surface 把 SDL child reparent 到该 HWND，定位为 `(0,0)`、尺寸等于 target，并由 surface 释放 SDL child。现有平台层尺寸变化本就释放并重建 surface，因此不必立即扩充公共 `IRenderSurface` 的 resize API。`DeviceWindowHandle` 传入 MonoGame 时仍必须是 SDL 指针；父 HWND 只用于挂载。与上方“直接返回 SDL HWND”的长期结构相比，这条兼容路径可先保留现有平台层的焦点、捕获和输入分发。

**消息泵只保留 Avalonia 一份。** `MonoGame.Library.SDL` 2.32.2.1 的包提交 `4fcf6abae791c2cfdae6ce0acc13f33b9efdd180` 固定 SDL 子模块 `e11183ea6caa3ae4895f4bc54cad2bbb0e365417`。该源码的 `WIN_PumpEvents` 只有在 `SDL_WINDOWS_ENABLE_MESSAGELOOP` 为真时才运行 Windows `PeekMessage/TranslateMessage/DispatchMessage` 循环。因此嵌入宿主应在 SDL 视频初始化前设置该 hint 为 `0`，由 Avalonia Dispatcher 负责系统消息，不能在每个 viewport timer 内再运行 `Game.Run` 或独立 SDL message loop。[固定 SDL 事件实现](https://github.com/libsdl-org/SDL/blob/e11183ea6caa3ae4895f4bc54cad2bbb0e365417/src/video/windows/SDL_windowsevents.c)、[官方 hint](https://wiki.libsdl.org/SDL2/SDL_HINT_WINDOWS_ENABLE_MESSAGELOOP)

`SDL_PollEvent` 隐含调用 `SDL_PumpEvents`。图形宿主可以统一用 `SDL_PeepEvents(..., SDL_GETEVENT, ...)` 排空已由窗口过程产生的 SDL 事件，或在明确关闭 Windows message loop 的前提下按需维护 SDL 状态；不能每个视口分别消费全局队列。SDL 的 `WM_WINDOWPOSCHANGED` 原过程会同步读取 client rect 并更新 SDL window 尺寸，子类不能吞掉这个消息；即使不运行 SDL 的 Windows message loop，Avalonia 的 `DispatchMessage` 仍会调用 SDL HWND 的窗口过程。`SDL_SetWindowsMessageHook` 处于 SDL 自己的 TranslateMessage 路径，在该循环被禁用后不是可靠的嵌入转发入口。[PumpEvents](https://wiki.libsdl.org/SDL2/SDL_PumpEvents)、[PeepEvents](https://wiki.libsdl.org/SDL2/SDL_PeepEvents)、[Windows hook](https://wiki.libsdl.org/SDL2/SDL_SetWindowsMessageHook)

**保留 STATIC 输入宿主时，SDL child 应是只绘制的窗口。** 可在同一个 UI／图形线程对 child 使用 `SetWindowSubclass`，对 `WM_NCHITTEST` 返回 `HTTRANSPARENT`，让命中测试落到下面的 STATIC；同时禁止 child 激活和抢焦点，保留 parent 的 `SetFocus`／`SetCapture`。固定 SDL 有 `SDL_WINDOW_NO_ACTIVATION_WHEN_SHOWN=1`，其 `WIN_ShowWindow` 也尊重 `WS_EX_NOACTIVATE`。HTTRANSPARENT 只负责同线程命中测试，不能代替键盘、捕获、焦点丢失与销毁处理；必须验证实际父子窗口层级下命中的是预期 STATIC，而不是旁边控件。[命中测试语义](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)、[固定窗口显示实现](https://github.com/libsdl-org/SDL/blob/e11183ea6caa3ae4895f4bc54cad2bbb0e365417/src/video/windows/SDL_windowswindow.c)

如果选择显式消息转发，应只转发定义清楚的输入消息到所属 viewport，鼠标 client 坐标经 `MapWindowPoints(child,parent,...)` 转换，滚轮的 screen 坐标保持原义；键盘 repeat／system-key flags 保留。一次消息只能经平台事件进入运行实例一次，不能同时消费 SDL 对应事件。`WM_WINDOWPOSCHANGED`、`WM_SIZE`、DPI、paint 和销毁链仍交给 `DefSubclassProc`／原 SDL WndProc，不可笼统拦截所有消息。子类委托和窗口代次归 surface 生命周期持有，释放前在创建线程解除子类，之后再 `SDL_DestroyWindow`，最后才释放宿主视频子系统；不要由 Avalonia 和 SDL 各销毁同一 HWND。[Win32 子类线程及生命周期约束](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)

这些接入推论由固定源码和官方接口支持，尚未在正式 Avalonia 宿主实测。现有 MonoGame 补丁无需为了 hint 或 subclass 再改上游源码；下一步应先在当前平台契约内验证单消息泵、命中测试／焦点／捕获、切换文档与失效代次，再决定是否需要公开 native surface 创建接口。
