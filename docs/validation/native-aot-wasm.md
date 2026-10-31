# WASM / NativeAOT 实证（2026-10-31）

结论：**当前 Wasmtime 48.0.2 绑定不满足本仓库严格 NativeAOT 发布门槛**。真实发布在 AOT／裁剪分析阶段失败，不能将它登记为已支持。仅用于研究的原生产物能在本机及无系统 .NET 的干净 Windows 通过当前执行器的 15 项检查；这证明固定签名路径具有可行性，不证明 Wasmtime 全部 API 兼容，也不代替完整游戏发布验收。

## 输入、工具链与复现

- Windows 10.0.26200、x64；仓库 SDK 10.0.112，ILCompiler / 运行库 10.0.12。
- NuGet Wasmtime 48.0.2，包记录的源码提交为 `d9db6b0005b93ba09a6ada8ddce2d5a3846511eb`；选用 `lib/net10.0/Wasmtime.Dotnet.dll` 和 `runtimes/win-x64/native/wasmtime.dll`。
- [探针项目](../../tools/AotProbes/Wasm/Wasm.csproj)直接编译未修改的 `src/Wasm.Execution/*.cs`，没有复制／放宽执行器。测试对象是当前 `ReadInt` / `ApplyInt` 固定签名、实际 `samples/wasm-counter/counter.wasm` 以及限制用反例。探针不链接桌游、Editor 或整套 Runtime。
- 没有程序集整体保留、`NoWarn`、关闭裁剪或关闭 AOT 分析。探针的 `IsAotCompatible` 用于开启分析，不是给生产 Wasmtime 插件设置兼容标识。

在仓库根运行：

```powershell
pwsh -NoProfile -File tools/AotProbes/Wasm/run.ps1 -Output artifacts/native-aot/wasm/repro-new
```

脚本要求新证据目录，分别使用独立 `--artifacts-path` 执行普通、严格 AOT 和诊断 AOT 发布。严格构建保留仓库 `TreatWarningsAsErrors=true`；诊断构建只将警告视为非错误，以取得运行证据，**不得作为发行路径**。原始结果位于 `artifacts/native-aot/wasm/repro/`：

| 证据 | 实际结果 |
| --- | --- |
| `summary.json` | SDK 10.0.112；严格发布退出 1；普通／诊断 AOT 运行均退出 0 |
| `strict-aot-build.log` | 以下四类未解决分析诊断阻止发布 |
| `diagnostic-aot-build.log` | 同样的警告保持可见，真实原生链接成功 |
| `jit-result.json` | 15 / 15，`dynamicCodeSupported=true` |
| `aot-result.json` | 15 / 15，`dynamicCodeSupported=false`；进程模块含本发行目录 `wasmtime.dll`，不含 `coreclr.dll`、`clrjit.dll`、`hostfxr.dll` |
| `files.json` | 原生产物的大小与 SHA-256 |

`RuntimeFeature.IsDynamicCodeSupported=false` 和模块清单确认运行的是原生宿主，不是普通 .NET 成功冒充 AOT。本机安装了 .NET，因此另外在 Windows Sandbox 的 Windows 10.0.26100 / x64 运行同一诊断产物：`dotnet`、64 位与 32 位系统 .NET 安装检测均为 false，运行退出 0、15 / 15 通过，进程只加载发行目录 Wasmtime 与系统原生 DLL。

精简证据已入库：[构建摘要](evidence/native-aot-wasm/build-summary.json)、[严格构建日志](evidence/native-aot-wasm/strict-aot-build.log)、[产物哈希](evidence/native-aot-wasm/files.json)、[干净环境](evidence/native-aot-wasm/clean-environment.json)、[干净运行报告](evidence/native-aot-wasm/clean-result.json)、[退出码](evidence/native-aot-wasm/clean-exit.json)。其中退出码从共享 `artifacts/native-aot/clean-evidence/result.json` 提取 WASM 项，其他证据为对应原文件副本。

## 编译阻碍

展开 `TrimmerSingleWarn=false` 后，严格构建报告：

| 诊断 | 归属及原因 |
| --- | --- |
| IL3050 | `Wasmtime.ValueRaw.Converter<T>()`、`ReturnTypeFactory<T>.Create()` 调用 `Type.MakeGenericType`，动态实例化的代码不能由分析器保证 |
| IL2055 | `ReturnTypeFactory<T>.Create()` 的 `MakeGenericType` 无法静态分析 |
| IL2072 | `ReturnTypeFactory<T>.Create()` 传给 `Activator.CreateInstance` 的类型不保证公开无参构造函数 |
| IL2070 | `TypeExtensions.TryGetResultInterface(Type)` 调用 `GetInterfaces` 的入参没有对应元数据约束 |

依据是本机实际 ILC 输出，源码也可复核对应的 [ReturnTypeFactory](https://github.com/bytecodealliance/wasmtime-dotnet/blob/v48.0.2/src/ReturnTypeFactory.cs) 与 [ValueRaw](https://github.com/bytecodealliance/wasmtime-dotnet/blob/v48.0.2/src/ValueRaw.cs)。后续应限定／替换当前整数签名的动态适配路径或采用已验证的绑定修正，并重新执行严格构建；不能因为本次小样例运行成功就压掉全部告警。

现有 `WasmtimeTrapCompatibility` 对 `Config.NativeHandle` 的反射访问已经有限定的 `DynamicDependency`。本次普通和原生运行均通过四种 catchable trap，未通过关闭 CET 或移除显式检查绕过崩溃。上游 [PR #390](https://github.com/bytecodealliance/wasmtime-dotnet/pull/390) 说明 `WithSignalsBasedTraps(false)` 对 Windows CET 的作用；本次仍使用固定 48.0.2 的原有 shim，没有擅自升级依赖。

## 已运行的行为

普通和诊断 AOT 均通过以下同一组检查：

1. 实际 counter 样例的 `bgb_api`、`bgb_version`、`bgb_init`，以及连续三次 `bgb_command(当前值, 1)` 得到 3。
2. 每次调用新建 Store；全局可变状态不串用；调用结束后的 `WasmCall` 失效，重复 Dispose 安全，释放后执行被拒绝。
3. 普通 host import 和 WASI import 都被拒绝；非法字节、超尺寸内容与重复身份被拒绝。
4. 无限循环耗尽 fuel，初始超内存被拒绝，`memory.grow` 超额返回 -1。
5. 越界内存、`unreachable`、除零和栈溢出分别产生预期 `TrapException`，进程保持可继续运行。
6. 连续创建、执行、释放 64 个执行器成功。这是生命周期冒烟，未做长期泄漏断言。

边界：生产执行器是同步 `Invoke`，没有 `CancellationToken` 或 epoch 取消入口。本次验证的是现有确定性 fuel 中断，不冒称已提供任意时刻取消。Wasmtime 官方分别说明 [fuel 和 epoch 中断](https://docs.wasmtime.dev/examples-interrupting-wasm.html)；若正式发布要保证取消进行中的执行，需要在共享调用契约中实现并重新验证。

本探针不绕过或修改上层的权限与 SHA-256 检查；这些仍位于 `BoardGame.Wasm/WasmPlugins.cs` 和正式内容加载链。它也没有把复制文件后的 SHA-256 相等冒充完整 AOT 游戏加载、权限、内容篡改或回放测试。上层公共 JSON／静态装配与完整游戏结果仍由后续适配及联合验收负责。

## 产物、原生 ABI 与平台边界

本次 `repro/diagnostic-aot/` 文件：

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `Wasm.AotProbe.exe` | 2,254,848 | `416BB6E3DF34DE98CD2DB5DA6A5456C4E7213BA42D7E4D143299ED1F523FB361` |
| `wasmtime.dll` | 22,092,288 | `19B3880FBB92F1826C58AC9F5E2A472692B5FC3D012A1A7A41983CCF77544607` |
| `counter.wasm` | 134 | `386DD6D65D4753BDD831857013B50AAD30CCC2C2A6938CBC01142B13C50D3C26` |

另有调试 PDB（10,694,656 字节），不属执行必需文件；没有普通 .NET 运行时文件。最终发行还须按目标环境校验 native DLL 的系统依赖，不能只复制托管引用程序集。

.NET NativeAOT 编译 C# 宿主；包中的 `wasmtime.dll` 仍在 `Module.FromBytes` 时编译 Wasm。这条路径不要求额外 `.cwasm` 预编译，且不承诺整个进程没有 JIT。Wasmtime 的 [预编译机制](https://docs.wasmtime.dev/examples-pre-compiling-wasm.html)是另一种需要单独规定产物信任、版本、目标及 CPU 兼容的部署方式，本次没有引入它。

本次只覆盖 Windows x64 固定包和当前整数 ABI。P/Invoke 使用 native Wasmtime C ABI，包括原有 `cdecl` / 单字节 bool 的配置调用；跨平台必须交付相应 native 库并验证入口、调用约定、布局、字符编码及资源所有权。NativeAOT 默认仍允许运行时绑定原生库，见 [.NET 原生互操作规则](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)。这不等同于允许运行时加载未知托管 DLL，也不构成任意第三方 C ABI 插件导入协议。

## 判定

维持 Wasmtime 48.0.2 当前生产绑定的 AOT **未获支持** 状态。已取得可复现的严格编译阻碍和干净 Windows 上的有限运行证据；未完成完整游戏公共链路、主动取消及长期资源验证。后续不得将本探针 `success=true` 直接转为生产 AOT 兼容声明。

## 固定整数 ABI 的非泛型候选

后续隔离实验在不修改 `src/` 的前提下找到了避免上述诊断的路径：[WasmUntyped](../../tools/AotProbes/WasmUntyped/WasmUntyped.csproj) 通过脚本读取当前生产执行器，仅替换 `WasmCall` 类，保留原有引擎配置、权限边界、imports 拒绝、fuel／内存限制、Store 隔离和释放实现。生产源码 SHA-256 为 `80C2BA7C31017389B1F5BABADCE6AE916E3B5A104C5A1AE788259A75438CBE3C`。

候选调用非泛型 `Instance.GetFunction(name)`，先检查参数数量及所有参数为 `Int32`、结果恰好一个 `Int32`，再调用 `Function.Invoke()` 或 `Function.Invoke(ValueBox[])`。它不使用 `CheckTypeSignature(Type)`，避免重新进入反射类型推断；错误签名在执行来宾代码前拒绝。此调用形态直接使用 [固定版本 Function.cs 的公开接口](https://github.com/bytecodealliance/wasmtime-dotnet/blob/v48.0.2/src/Function.cs)，没有修改或整体保留第三方库。

复现：

```powershell
pwsh -NoProfile -File tools/AotProbes/WasmUntyped/run.ps1 -Output artifacts/native-aot/wasm/untyped-new
```

本次结果位于 `artifacts/native-aot/wasm/untyped-verified/`：普通与 **严格 NativeAOT** 构建和运行全部退出 0，AOT 编译无警告，未覆盖 `TreatWarningsAsErrors`、未抑制任何分析诊断。普通／原生均通过 18 / 18，包括原来的 15 项及新增的缺少导出拒绝、参数／返回类型和数量不符时执行前拒绝、有符号整数及 i32 回绕语义。原生进程动态代码为 false。

入库证据：[构建摘要](evidence/native-aot-wasm/untyped-summary.json)、[严格构建日志](evidence/native-aot-wasm/untyped-strict-build.log)、[本机原生运行](evidence/native-aot-wasm/untyped-native-result.json)、[产物清单](evidence/native-aot-wasm/untyped-files.json)。候选 EXE 为 2,249,216 字节，SHA-256 `BEDD904138A37E666283E2BA0C8A1B6741C212F21D081D337C023EC49A6555AC`；native Wasmtime 和样例内容不变。

随后使用这一精确候选在同一无 SDK／系统 .NET 的 Windows 10.0.26100 / x64 Sandbox 再次执行：18 / 18 通过、退出 0、动态代码为 false。证据为[候选干净运行报告](evidence/native-aot-wasm/untyped-clean-result.json)与[退出码](evidence/native-aot-wasm/untyped-clean-exit.txt)，环境沿用上述 `clean-environment.json`。这是候选自身的运行证据，没有用前一节不同 EXE 的结果替代。

这项实验说明可以保留 Wasmtime 48.0.2 和现有沙箱语义，按固定整数 ABI 改写调用绑定。它尚未接入生产代码，正式共享链路、主动取消及整局验收仍须由后续任务完成，不扩展为 Wasmtime 全部 API 的兼容保证。
