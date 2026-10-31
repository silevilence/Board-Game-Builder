# Lua 与工作进程 NativeAOT 实证

2026-10-31。对应 ROADMAP「完成 Lua 与工作进程的 NativeAOT 实证」。本项研究已取得真实 AOT 构建及无系统 .NET 的干净 Windows 失败证据，结论为：**当前 MoonSharp 2.0.0 与仓库 Lua 执行链不支持 win-x64 NativeAOT 发行**。普通运行通过；诊断模式能够生成原生程序，但真实规则、CPU、流程与工作进程协议均有可复现失败。没有修改正式执行器、保留整库、关闭裁剪或启用反射序列化来掩盖问题。研究任务的“不支持”结论不表示发行能力通过验收。

## 构建环境与复现

- Windows 10.0.26200，x64；仓库固定 SDK 10.0.112、目标 net10.0；Visual Studio 18.6.3 C++ 工具已安装。
- 依赖为仓库 `src/Lua/Lua.csproj`、`src/Lua.Execution/Lua.Execution.csproj` 的实际项目引用与 MoonSharp 2.0.0。探针直接引用生产 `LuaExecution` / `LuaProcessSession`，没有复制执行器。
- [探针](../../tools/AotProbes/Lua/Program.cs)携带 quick-cards 的真实规则／CPU 脚本和 flow-cards 的真实流程脚本，输出随包保存脚本，不需要源码目录即可运行。
- 一次完整复现：`pwsh -NoProfile -File tools/AotProbes/Lua/verify.ps1 -Output artifacts/native-aot/lua/new-run`。输出目录必须不存在；脚本不覆盖或清理既有证据。探针失败时脚本返回错误，不能当作发行成功。
- 本次完整证据保存在 `artifacts/native-aot/lua/verified/`：四份构建日志、四份运行日志及各自退出码，`dotnet-info.txt`、`source-head.txt`、`native-files.json`。该目录属于本地验收产物，不纳入 Git；本文件保留可复核结论和命令。
- 干净 Windows 验证日志位于 `artifacts/native-aot/clean-evidence/`，输入为隔离的 `clean-input` 原生产物及脚本，不含源码或作者工具。必要证据已保存在 [evidence/native-aot-lua](evidence/native-aot-lua/)：环境检查、原生失败日志、输入请求、退出码、原生文件 SHA-256，以及普通运行对照结果。

发布使用 `dotnet publish -c Release -r win-x64 --self-contained true`，分别指定独立 `--artifacts-path` 和输出目录。原生版本额外传入 `PublishAot=true`。为取得完整问题集合，**仅此诊断调用**传入 `TreatWarningsAsErrors=false`、`IlcTreatWarningsAsErrors=false`、`TrimmerSingleWarn=false`；警告完整保留，正式工程的警告策略不变。这些输出是失败分析产物，不是兼容发行物。

## 实测结果

| 路径 | 普通运行 | NativeAOT |
| --- | --- | --- |
| quick-cards rules/actions | 通过，得到 start 操作与中文标签 | 输出 JSON 时失败：反射序列化被禁用 |
| quick-cards rules/decide | 通过，得到洗牌、发牌和进入 play 的命令 | `ipairs` 为 nil，脚本运行失败 |
| quick-cards cpu/decide | 种子 7 两次结果一致，选择 b | `ipairs` 为 nil，脚本运行失败 |
| flow-cards turn/seat、judge | 通过，分别返回 p1、again | 输出 JSON 时失败 |
| flow-cards turn/next | 通过，返回抽牌和 next 命令 | `table.insert` 为 nil，脚本运行失败 |
| 无限循环指令预算 | 通过，返回 instruction budget exceeded | 直接执行器同样拒绝；不证明工作进程可用 |
| 显式 Lua.exe 工作进程 | 六项真实脚本与指令预算通过 | 七项请求均在协议交换阶段失败，管道 EOF |
| 请求发出前取消 | 未启动工作即取消 | JIT 调用端同样提前取消；没有验证 AOT worker 活动取消 |

每个正常脚本在全新 VM／会话中重复相同种子。直接执行器与普通工作进程结果相同；原生结果失败，因此本次**不能完成完整对局动作、结果和录制的等价性验收**。直接探针退出码为 JIT 0 / AOT 1，工作进程调用探针为 JIT 0 / AOT 1。

原生 `Script(CoreModules.Preset_HardSandbox)` 的实际登记结果为 `ipairs=Nil; table.insert=Nil; math.ceil=Nil`；同一探针普通运行均为 ClrFunction。原生运行 `_MOONSHARP.platform` 仍报告 `core.dotnet.clr4.netcore`，`is_aot=false`。这些观测解释了平台标识不能充当 .NET NativeAOT 兼容保证。

原生 `Lua.exe` 单次 JSON 请求的 stderr 指向 `BoardGame.Kernel.Wire.Json<T>`：`InvalidOperationException`，反射序列化关闭。错误响应本身仍调用同一序列化路径，故无法返回结构化失败；退出码为 -1073740791。生产 `LuaProcessSession` 观察到 EOF 并回收进程。构建输出包含原生 exe 与调试符号，没有 coreclr.dll、hostfxr.dll 或托管依赖 DLL。

在新建 Windows Sandbox（OS 10.0.26100）中，[环境记录](evidence/native-aot-lua/environment.json)的 `dotnet`、`runtime`、`runtimeX86` 均为 false。相同原生探针实际启动后仍以 1 退出，六项脚本失败；独立原生 worker 接收记录的请求后以 -1073740791 退出，stderr 同样定位到 `Wire.Json<T>`。因此失败不依赖开发机源码或系统 .NET，也不能归因于缺少 .NET 安装。[直接探针输出](evidence/native-aot-lua/lua-direct.stdout.log)、[worker 错误](evidence/native-aot-lua/lua-worker.stderr.log)、[退出码](evidence/native-aot-lua/result.json)、[实际输入文件清单及哈希](evidence/native-aot-lua/native-files.json)均已保留。

## 已定位的适配边界

1. **JSON 输入、输出及错误路径。** `LuaJson.Json/Read`、`LuaExecution.Invoke` 的任意对象结果、worker 的 `Wire.Read/Json` 依赖反射；`Wire` 的 SortedDictionary 转换器还使用动态泛型构造。需显式 JSON 契约／源生成或有界手写 JSON 输出，并保持现有大小、深度、数组与错误协议。Microsoft 要求 NativeAOT 的 System.Text.Json 使用源生成；这与本次运行错误一致。[官方说明](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation)
2. **MoonSharp 模块登记。** 固定版本源码的 `ModuleRegister.RegisterModuleType` 通过枚举方法、属性及字段登记核心模块；这些方法并没有被当前静态引用保留。本次观察到实际缺失的函数，不能只保留 `Script` 构造器。后续应研究精确静态登记／必要元数据，覆盖完整获准沙箱模块，不能通过保留整个 MoonSharp 程序集宣布裁剪完成。[v2.0.0.0 源码](https://github.com/moonsharp-devs/moonsharp/blob/v2.0.0.0/src/MoonSharp.Interpreter/Modules/ModuleRegister.cs)
3. **平台与宿主回调。** MoonSharp 的 hardwire 文档讨论 IL2CPP／旧 AOT 平台及 UserData 注册类型，不保证 .NET NativeAOT 的全部路径；现有宿主使用 `DynValue.NewCallback`，未注册任意业务对象，本次仍触达模块反射与泛型分析警告。需要明确 NativeAOT 平台策略和允许的宿主互操作范围。[MoonSharp 官方 hardwire 文档](https://www.moonsharp.org/hardwire.html)
4. **独立 worker。** 普通 `LuaProcessSession` 可回退 dotnet，`LuaWorkerSession` 使用 `Assembly.Location`；原生发行必须单独提供严格的随包入口定位。探针先验证显式 exe 存在，调用真实 `LuaProcessSession`，本次没有依赖 dotnet 回退。

worker 构建日志实际包含 IL2026、IL2055、IL2060、IL2067、IL2072、IL2075、IL3000、IL3050。涉及反射序列化、动态泛型、MoonSharp 的成员枚举／泛型集合转换以及单文件中的程序集位置。不能以最后的 publish 成功行忽略这些阻碍。

## 受阻的支持能力验收

当前版本的原生 worker 在输入／错误序列化阶段即失败，尚无法验证活动请求超时、内存上限、活动取消、重复复用、父进程退出及实际对局记录。直接执行器的指令预算成功、普通工作进程的通过结果，均不能替代这些原生安全边界验证。干净 Windows 已复现启动及执行失败，足以完成本项研究允许的“不支持”结论；受阻的安全边界测试不能被记作通过。

后续在保留沙箱与预算的前提下修复上述路径，再运行相同探针和完整规则／CPU／流程对局回归，补齐工作进程生命周期与干净环境证据。在此之前，发布兼容矩阵应拒绝选择此 Lua 版本的 NativeAOT 发行；这不是对 Lua 语言或所有解释器的普遍不兼容结论。

## 最小适配候选（研究建议，未实施）

JSON 可先在 Lua.Execution 内消除对通用 `Wire` 的需求：为 `LuaRequest`、`LuaResponse`、`LuaCallShape` 使用独立源生成上下文；对已经受深度／条目／类型限制的 Lua 数据结果，直接用 `Utf8JsonWriter` 输出。这样保留 null、数值、字符串、布尔、数组／对象和现有预算语义，不必为开放的 `object` 图或整个 Kernel 建反射保留表。协议仍需统一 camelCase、未知字段拒绝及 api 必填规则，正常／失败响应都走同一已注册编解码器。

MoonSharp 的当前 HardSandbox 只包含 GlobalConsts、TableIterators、String、Table、Basic、Math、Bit32；对应需要反射登记的方法／字段集中在七个核心模块类型（Table 对应两个类型）。可先试验精确保留这些类型的注册成员，或生成静态登记代码，保持当前禁用 print、collectgarbage、math.random／randomseed 的策略；无需因为这个失败立即更换 Lua 实现或保留整个库。这是由固定版本源码推导的修复候选，尚无修复后 AOT 通过证据。[固定版本预设](https://github.com/moonsharp-devs/moonsharp/blob/v2.0.0.0/src/MoonSharp.Interpreter/Modules/CoreModules.cs)

平台误判的源头是旧 `PlatformAutoDetector` 用表达式树 `Compile()` 是否抛异常来推断 AOT；现代 NativeAOT 可解释表达式，故这一推断不再可靠。`IPlatformAccessor.IsRunningOnAOT()` 有可覆盖入口，可研究由 `RuntimeFeature.IsDynamicCodeSupported` 明确选择平台，而非读取旧探测值。仍须审计第三方内部直接使用静态探测器的路径，不能只改展示标识。[平台探测源码](https://github.com/moonsharp-devs/moonsharp/blob/v2.0.0.0/src/MoonSharp.Interpreter/Platforms/PlatformAutoDetector.cs)、[可覆盖入口](https://github.com/moonsharp-devs/moonsharp/blob/v2.0.0.0/src/MoonSharp.Interpreter/Platforms/PlatformAccessorBase.cs)

上述改动能针对已见故障缩小范围，但不等同于整个 MoonSharp 的 AOT 兼容证明。当前编译还报告通用 CLR 互操作、动态泛型集合及类型枚举警告；需要逐条证明不可达或提供准确注解，必要时维护固定版本补丁。不能承诺仅添加几条裁剪根便能满足完整发布要求。本阶段保留不支持结论，不将未验证候选写入正式执行器。
