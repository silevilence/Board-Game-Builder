# NativeAOT 依赖、平台与构建环境研究

核对日期：2026-10-31。需求依据：`ROADMAP.md`「NativeAOT 游戏发布、可选渲染后端与外部 .NET 运行插件」中的支持矩阵任务。本文件记录研究和代码检查，**不把研究结果当作原生运行验收**；GL、Lua、WASM 的实际构建、运行及干净环境结果由各自实证记录补充。

## 结论与证据等级

首轮候选目标仍为 Windows x64，本机构建。现有 WindowsDX 继续用于普通发布，不进入 AOT 候选；DesktopGL 3.8.4.1 需要独立窗口和嵌入视口两条实证。没有证据支持把当前整条 Runtime、Lua 或 WASM 链直接标为 AOT 兼容。此次研究不升级依赖、不更换 MonoGame，也不扩大交付平台。

| 等级 | 必需证据 | 不能推出的结论 |
| --- | --- | --- |
| 普通运行通过 | 固定版本的 JIT／self-contained 运行记录 | 能被裁剪或 AOT 编译 |
| 声明兼容 | 包作者声明、目标 RID、静态入口、依赖及分析器配置 | 所有 API 或组合都能运行 |
| AOT 编译通过 | 原生链接成功、完整警告及处理记录、产物清单 | 中文、输入、恢复和清理正确 |
| 目标系统运行通过 | 对该产物执行功能及失败路径验证 | 不依赖开发机安装 |
| 干净环境通过 | 无 SDK、系统 .NET、源码及作者安装的系统中执行同一产物 | 未测试的 RID 或插件版本受支持 |

普通路径的既有证据见 [应用循环兼容矩阵](../validation/application-compatibility.md)。本研究没有重新执行这些回归，也没有新增任何组合的“干净环境通过”声明。

## 版本与依赖矩阵

版本由仓库 `global.json`、各 `.csproj`、本地 NuGet `.nuspec` 与 `obj/project.assets.json` 交叉核对；不以官网最新版本替换项目固定版本。下表的“待验证”是准入前工作，不能作为支持标识。

| 组件／确切版本 | 本项目使用路径与已确认约束 | AOT 结论／后续动作 |
| --- | --- | --- |
| SDK 10.0.112；TFM `net10.0`／`net10.0-windows` | `global.json` 使用 `latestPatch`；`Directory.Build.props` 开启 warnings-as-errors。当前 `dotnet --info` 选择 10.0.112，Host 10.0.12 | .NET 提供 NativeAOT；仍须记录实际 ILCompiler、SDK 和 native linker 版本，不把 SDK 安装当作游戏发布成功。[微软部署说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| CordiSharp 0.2.0 | `Application.Core` 引用核心包并排除包内 analyzers；`.nuspec` 固定源码提交 `7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f`。上游既有显式委托／对象入口，也有类型反射与程序集加载入口 | 只验证显式注册、服务、事件、fiber 和释放路径；不能把生成元数据理解为所有反射已消失。固定版本项目未直接设置 `IsAotCompatible`；实际分析与运行待证。[固定源码与入口说明](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/README.md) |
| Avalonia.Desktop 12.1.1；CordiSharp.Plugins.Avalonia 0.2.0 | Editor 保持作者侧；`Rendering.Avalonia`／`Player.Hosting` 的平台窗口可进入游戏候选。资产解析结果包含 Avalonia 12.1.1、SkiaSharp 3.119.4 和 Angle natives `2.1.27548.20260419` | Avalonia 有官方 NativeAOT 路径，仍须验证本项目窗口、输入和原生依赖。CordiSharp.Plugins.Avalonia 只属 Editor，不得随游戏带入。官方建议编译绑定及避免动态 XAML／反射服务发现。[Avalonia AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |
| MonoGame.Framework.WindowsDX 3.8.4.1；SharpDX 4.0.1 | `.nuspec` 引用 `Microsoft.WindowsDesktop.App.WindowsForms` 和 SharpDX 系列。`WindowsDxBackend` 已声明 AOT false | 拒绝 AOT 候选。MonoGame 官方明确 WindowsDX 与 AOT／裁剪不兼容并指向 DesktopGL；无需以强行关闭分析替代后端适配。[发行说明](https://docs.monogame.net/articles/getting_started/packaging_games.html#publishaot-and-publishtrimmed) |
| MonoGame.Framework.DesktopGL 3.8.4.1（候选，非现有后端） | 与 WindowsDX 同版本。固定源提交 `f34200720b558125964273fd3e7ab44cde0b0429` 的 GL 设备使用 `SdlGameWindow.Instance.Handle` | 独立 `Game` 可作为最小候选；任意 HWND 与多视口不能靠换包获得。需验证 SDL、GL context、图形线程、纹理及窗口生命周期。[固定 GL 实现](https://github.com/MonoGame/MonoGame/blob/f34200720b558125964273fd3e7ab44cde0b0429/MonoGame.Framework/Platform/Graphics/GraphicsDevice.OpenGL.cs) |
| System.Drawing.Common 10.0.0 | `WindowsDraw2D.Text` 未配置字体时使用 GDI+／Microsoft YaHei UI；Authoring 和 Editor.Core 也引用此库 | 官方限定 Windows；不能据其存在单独断言 Windows AOT 不可用，但不能让 GL 跨平台契约依赖系统字体。作者端依赖应与最小运行产物分离。[平台限制](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only) |
| SkiaSharp 3.119.4；NativeAssets.Win32 3.119.4 | `PackagedTypeface` 从游戏资源加载字体并生成 PNG，避免依赖作者机器安装的字体。NuGet 固定提交 `f568ac94dd768ef9a2f593537cfde2dd0d348ef5` | 可复用为中文栅格化候选，须随所选目标收集 `libSkiaSharp`，验证缺字、透明度、资源释放；不是 GL 原生实证的替代。[固定版本发布记录](https://mono.github.io/SkiaSharp/docs/releases/3.119.4.html) |
| System.Text.Json（.NET 10 框架提供） | `Application.Core/ContentJson.cs`、`Lua.Execution/LuaJson.cs` 等仍用反射式泛型序列化；`Kernel.Abstractions/Model.cs` 转换器使用 `MakeGenericType`＋`Activator.CreateInstance` | 必须建立闭合类型的 source-generated metadata／显式转换器，并处理 `object` 的实际类型集合。不得靠恢复全局反射序列化或整库保留宣布兼容。[源生成说明](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation) |
| YamlDotNet 18.1.0 | `Modules/AuthorConfiguration.cs` 用 `DeserializerBuilder`／`SerializerBuilder`，用于作者 JSON/YAML 转换；包提交 `748334a8fa7c227740018b284b71ad95cc6b7fc7` | 优先将作者解析从最小游戏依赖隔离；确需运行时 YAML 时单独采用并验证静态生成路径。上游动态 builder 明示需要静态生成方案，不应自动保留全部反射模型。[固定 builder 源码](https://github.com/aaubry/YamlDotNet/blob/748334a8fa7c227740018b284b71ad95cc6b7fc7/YamlDotNet/Serialization/DeserializerBuilder.cs) |
| MoonSharp 2.0.0（nuspec 版本 2.0.0.0） | `LuaExecution` 使用 `Preset_HardSandbox`、直接 `DynValue` callback、协程指令预算；结果仍经 `object` JSON 转换，工作进程协议也需静态编解码 | 解释器不等于 NativeAOT 自动兼容。模块反射注册、平台探测、回调元数据及 JSON 须实证。MoonSharp 的 hardwire 文档说明可减少反射，但不是本项目 .NET 10 验收。[官方 hardwire](https://www.moonsharp.org/hardwire.html)、[平台访问器](https://www.moonsharp.org/platforms.html) |
| Wasmtime 48.0.2 | 包提交 `d9db6b0005b93ba09a6ada8ddce2d5a3846511eb`，选用 net10.0 资产；包提供 win-x64／win-arm64／linux-x64／linux-arm64／osx-x64／osx-arm64 native 文件。`WasmExecutor` 只用固定整数签名，拒绝所有 imports | 只证明包携带这些 RID，不证明项目支持它们。当前 shim 反射 `Config.NativeHandle` 并保留非公开属性；仍需检查 AOT。上游 AOT/static-link PR 不能替代已发布包证据。[固定包源码](https://github.com/bytecodealliance/wasmtime-dotnet/blob/d9db6b0005b93ba09a6ada8ddce2d5a3846511eb/src/Wasmtime.csproj)、[AOT 提案](https://github.com/bytecodealliance/wasmtime-dotnet/pull/348) |

## 平台与最小构建环境

| 构建主机 → 目标 | 最小环境和限制 | 本项目状态 |
| --- | --- | --- |
| Windows x64 → `win-x64` | .NET SDK 10.0.112；Visual Studio 2022 或更新版本的 Desktop development with C++ 工作负载、x64 linker 与 Windows SDK；还原固定 NuGet／ILCompiler 包 | 首轮候选。本机 Windows `10.0.26200`，VS Community `18.6.11828.311`，VC Tools `14.44.35207`／`14.51.36231`，Windows SDK `10.0.19041.0`／`10.0.22621.0`／`10.0.26100.0` 已发现；工具存在不代表已选中，实际日志需记录最终版本 |
| Windows x64 → `win-arm64` | 另装 ARM64 C++ 工具链、确认每个原生库和图形适配，再在 ARM64 机器验收 | 未支持，未实测；不因 .NET 支持跨架构就开放 |
| Linux x64／arm64 → 对应 Linux RID | 对应 Linux 构建环境、clang 和发行版开发库；例如 Ubuntu 需要 clang／zlib1g-dev；GL 窗口系统和 native 库另行验收 | 后续研究；本组不承诺交付。系统基线和 libc 兼容需单独固定 |
| macOS x64／arm64 → 对应 macOS RID | macOS 构建环境、Xcode Command Line Tools；目标架构、签名及图形适配单独验证 | 后续研究；本组不承诺交付 |
| Windows → Linux／macOS | .NET NativeAOT 不支持直接跨操作系统编译；需要对应 OS 的独立 runner／VM（Linux 可评估 WSL），不能只换 RID | 拒绝直接跨系统构建；独立环境须有自己的验证记录 |

以上工具要求来自 [微软 NativeAOT 前提](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites) 和 [跨平台编译约束](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)。Windows 图形验收需要桌面会话及可用 GL 驱动，不能以无界面编译 runner 代替窗口、输入和 DPI 验收。普通预编译 Player 导出仍无需作者或玩家安装 SDK；新增工具要求仅属于 AOT 构建端。

## GL 原型必须回答的问题

当前 `Rendering.Avalonia/PlatformViewport.cs` 创建 Win32 `STATIC` 子 HWND、挂接窗口过程、处理输入并按 DPI／尺寸重建 surface；`WindowsDxBackend` 把这个 HWND 交给 `GraphicsDevice`。这是一条 WindowsDX 平台实现，并非已适配 DesktopGL 的公共窗口协议。

DesktopGL 固定版本的设备上下文使用全局 SDL 游戏窗口。SDL 的 `SDL_CreateWindowFrom` 能包装原生窗口，但 Windows/OpenGL 可能要求预设共享像素格式；该 API 的存在并不能证明 MonoGame 的全局窗口和 context 管理支持本项目多视口。[SDL 官方说明](https://wiki.libsdl.org/SDL2/SDL_CreateWindowFrom)

原型应分别记录：独立窗口创建／退出；一个嵌入视口；多视口交替 current／present；DPI、缩放、焦点、键鼠及关闭顺序。图片必须经过真实解码和 GPU 绘制，中文使用随包字体并验证缺字行为。资源计数在反复创建／关闭后回到基线；必要截图、输入结果和进程退出码一起保留。若固定版本公开 API 无法安全满足嵌入与多视口，应记录阻碍及所需调整，不能把独立窗口成功当作正式 GL 后端验收。

## Lua、WASM 与原生 ABI 实证边界

Lua 原型必须覆盖规则、CPU、流程的实际调用形状、种子随机、数组／对象结果、协议通信和拒绝路径。现有 `LuaProcessSession` 允许找不到 apphost 时回退 `dotnet`，原生发行必须关闭该回退，并验证随包 worker 定位、超时、取消、内存和指令预算、父进程结束后清理。不能仅把直接 `Script.RunString` 成功写成工作进程通过。

Wasmtime 编译 Wasm 使用原生 Cranelift；这是 .NET NativeAOT 之外的执行器。ROADMAP 不禁止它的原生 JIT，因此无需仅为“没有 .NET JIT”强制预编译 Wasm。若未来需要预编译，可评估 Wasmtime serialization/precompile，但产物绑定引擎版本与目标，需另外验证内容信任、兼容和加载边界。[Wasmtime 预编译说明](https://docs.wasmtime.dev/examples-pre-compiling-wasm.html)

当前 `WasmExecutor` 保留 `Fuel`、内存／表／实例数限制、无 imports／WASI、每次调用新 store 和锁内释放；兼容性 shim 关闭 signals-based traps 的原因与固定版本有关，不能为通过 AOT 删除。首轮只验证项目使用的 `GetFunction<int>` 和 `GetFunction<int,int,int>` 等固定签名以及错误、耗尽、取消／宿主清理。随包 `wasmtime.dll` 不应假定已静态链接进原生 exe。

其他原生库／解释器应显式固定 C ABI、调用约定、字段布局、字符串编码、所有权与释放函数、回调存活期、OS／架构及传递 native 依赖；未知托管 DLL 不可在 AOT 程序中动态加载。此处只定义研究边界，不增加通用 C ABI 插件导入交付。

## 兼容声明与后续验收门槛

库的 `<IsAotCompatible>true</IsAotCompatible>` 会启用裁剪、AOT 和单文件分析；.NET 10 增加相关程序集元数据及可选引用检查。它是作者声明和分析入口，不是运行认证。外部运行插件还须显式提供实现版本、RID、入口和依赖，区分实现程序集与 reference assembly；没有声明的外部插件不得进入候选。已声明的错误插件也必须被依赖检查、编译或实证阻止。[微软分析器与引用检查](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#aot-compatibility-analyzers)

后续证据按下列闭环保存，不静默换后端、不抑制全库警告、不保留整库以掩盖裁剪问题：

1. 固定仓库 revision、工程／包哈希、实际 SDK／ILCompiler／linker、目标 RID，收集完整发布日志和产物文件清单。
2. 三个隔离探针分别证明 GL、Lua worker、WASM 的实际调用路径，失败也保留可复现命令、退出码和阻碍。
3. 公共链验证 CordiSharp 生命周期、配置／内容／状态 JSON、加载身份、固定种子、记录与恢复；无脚本组合不得被 Lua／WASM 依赖拖入。
4. 原生产物在无源码、SDK、系统 .NET 与作者安装的 Windows 中验证；改变环境变量或在开发机删 PATH 不能冒充干净系统。
5. 最后通过同一 `ProjectView` 草稿完成 Editor 配置、撤销、试玩和三种导出方式的联合回归，再开放相应版本／平台组合。

## 2026-10-31 首轮探针状态

研究期间生成的本机证据如下；完整干净环境记录须另行补齐。

| 探针 | 当前实际结果 | 未通过部分与证据 |
| --- | --- | --- |
| `tools/AotProbes/Gl` | DesktopGL 3.8.4.1＋SkiaSharp 3.119.4 严格 AOT 发布成功；主代理报告本机三轮中文／图片绘制、resize 和 dispose 完成，动态代码支持为 false | 嵌入、多视口、真实用户输入及干净机尚不构成已通过结论；本机日志与图像在 `artifacts/native-aot/gl/` |
| `tools/AotProbes/Lua` | 原生调用失败；日志出现反射 JSON 被禁用，MoonSharp 核心函数 `ipairs`／`table.insert`／`math.ceil` 丢失；流程调用发生 nil function | 原生 worker 同样在协议 JSON 上失败；不能声明规则／CPU／流程兼容。见 `artifacts/native-aot/lua/` |
| `tools/AotProbes/Wasm` | 严格发布因 Wasmtime 的 IL3053／IL2104 失败；保留警告的诊断构建在本机 15 项固定签名、imports 拒绝、预算、trap 与释放检查通过，dynamicCodeSupported=false | 诊断日志包含动态泛型等 IL3050，不能作为正式兼容发布；结果及完整警告见 `artifacts/native-aot/wasm/` |

明确阻碍是 WindowsDX 的 WindowsForms 依赖、DesktopGL 的 SDL 全局窗口适配、反射式 JSON／动态泛型转换器、Lua 模块／工作进程路径，以及 Wasmtime 固定绑定的裁剪警告。2026-10-31 用户允许保留 MonoGame 并维护必要后端源码补丁；这为嵌入实证提供可选实施路径，不等于已经支持。是否可支持仍须由对应实证和正式实现任务给出证据，不能据此提前勾选后续任务。
