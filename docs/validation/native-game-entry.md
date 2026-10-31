# 专用原生游戏入口与运行库分发

`Player.Native` 提供固定内容桌面入口，复用 `ApplicationWindow`、实例驱动和通用视口。它不引用桌游、Editor、调试宿主或模块选择器。名称、玩法说明和视图来自游戏提供的 `IApplicationView`；发布参数提供 EXE 名称元数据、版本与可选 ICO，图标同时用于 EXE 和窗口。

`RuntimeSdk` 从预编译分发目录读取宿主配置及统一插件清单，检查文件身份，不在读取元数据时执行插件程序集。维护者工具 `tools/NativeSdk` 收集真实程序集引用闭包；作者构建只消费这些预编译实现及游戏快照。宿主配置按能力声明要求，通用桌面入口不限制具体游戏名或载荷格式。示例工具中的 Motion 组合只用于复现本项验收。

构建将发布时选定的宿主配置写入临时入口并固定实现身份，源内容保持不变。发行只有 `Play.exe`、游戏内容、所选原生依赖、可选图标和审计清单。运行时内容／原生库／图标的预期 SHA256 嵌入 EXE；修改外部清单不能授权内容变化。缺件或篡改时显示具体路径，并尝试写入 `startup-error.txt`。所有运行路径相对 `AppContext.BaseDirectory`，不读取作者机器的 SDK 或安装目录。

## 复现

先按已有文档构建固定 CordiSharp 与 MonoGame 补丁，再执行：

```powershell
dotnet build tools/NativeSdk -c Release -p:UseStaticRuntime=true --artifacts-path artifacts/native-sdk-build
dotnet artifacts/native-sdk-build/bin/NativeSdk/release/NativeSdk.dll pack artifacts/native-aot/sdk
dotnet artifacts/native-sdk-build/bin/NativeSdk/release/NativeSdk.dll motion artifacts/native-aot/sdk samples/motion-lab 'artifacts/native-aot/游戏 Motion'
```

最后一个命令可追加作者选定的 ICO 路径。分发和发行目标必须不存在。该维护者示例命令不进入最终游戏；统一作者操作与 Editor 接入由后续任务提供。

## 2026-10-31 实测

- 两轮严格 NativeAOT 发布通过，无 ILLink／ILC 警告降级。最终图标版 EXE 的 ProductName / FileDescription 为 `Motion lab`，ProductVersion 为 `1`。
- 无系统 .NET 的 Windows 10.0.26100 x64 沙盒中，发行目录复制后移至 `C:\原生游戏 Motion`，原路径不存在；窗口显示游戏图片与说明，方向键移动和空格复位可用，关闭后无残留进程。入口为 Windows GUI 子系统，启动／退出未出现控制台。
- 最终图标版在独立 `C:\原生游戏 图标 02` 目录启动，窗口及任务栏显示传入的游戏图标。
- 移走 SDL2.dll 得到 `missing required file: SDL2.dll`；修改 motion.json 得到 `content differs: content/motion.json`，两者都在设备启动前拒绝。关闭诊断后无残留进程。
- 输出无托管 DLL、coreclr、Lua、Wasm、WindowsDX、桌游和 Editor 专属实现；必要原生依赖为 SDL2、Skia、HarfBuzz 与 Avalonia ANGLE。输入引用清单和发行文件哈希可复核。
- 发布分析／完整性测试、22 项插件导入回归及项目边界检查通过。内嵌身份测试覆盖篡改外部清单不能绕过 EXE 中的预期值。

证据保存在 [evidence/native-game-entry](evidence/native-game-entry/)。本项验证专用入口及完整图形发行；桌游宿主组合、作者导出、Editor 和完整游戏组合联合验收仍按后续条目推进。
