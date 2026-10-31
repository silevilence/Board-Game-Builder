# 公共 Application.Core 的 NativeAOT 原型（2026-10-31）

当前正式源码加官方 CordiSharp 0.2.0 **不能直接严格 NativeAOT 发布**。先修复公共 JSON 的反射序列化后，仍在 CordiSharp 根上下文初始化时失败。隔离原型已经证明：固定版本 CordiSharp 的六文件补丁加公共 JSON 源生成，可以严格零告警编译，实际通过六项加载、打包、恢复、update 和生命周期检查；普通动态路径的七项针对性回归也与原包一致。

本记录是研究候选，尚未合并正式 `src/`，未声明整库、桌游或 Editor 的 AOT 兼容。代码位于 [tools/AotProbes/Core](../../tools/AotProbes/Core/)，精简证据在 [evidence/native-aot-core](evidence/native-aot-core/summary.json)。

## 范围与结果

探针编译真实 `Application.Core/*.cs` 和 `Application.Abstractions/*.cs`，使用自有定义加载插件与 update 累加器，通过正式 `GameRuntime.Load`、`GamePackage.Pack`、`SaveSnapshot`、`Restore`、`ApplicationCatalog.StartAsync` 和 `RuntimeHost` 运行。不引用桌游、Lua、WASM、渲染或 UI。生产文件未修改；候选是脚本生成的独立源码副本。

环境：Windows 10.0.26200 / x64，SDK 10.0.112，NativeAOT 10.0.12。CordiSharp 原包为 0.2.0，NuGet 元数据记录提交 `7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f`。对应 GitHub 源码 ZIP 的 SHA-256 为 `917DF179E460C799FDD0F083071223EE622E4CD16E3036BF1108130314D35A27`。

| 组合 | 编译 | 实际运行 |
| --- | --- | --- |
| 未修改公共源码 + 官方 NuGet，普通 .NET | 通过 | 6 / 6 |
| 未修改公共源码 + 官方 NuGet，严格 AOT | 失败，公共 JSON IL2026 / IL3050 | 无产物 |
| JSON 源生成副本 + 官方 NuGet，普通 .NET | 通过 | 6 / 6，指纹与原实现一致 |
| JSON 源生成副本 + 官方 NuGet，严格 AOT | 失败，CordiSharp 可达反射／JSON 诊断 | 无可发行产物 |
| JSON 源生成副本 + 官方 NuGet，诊断 AOT | 保持警告可见后得到原生产物 | 1 / 6；根初始化即抛反射序列化禁用异常 |
| JSON 源生成副本 + CordiSharp 固定源码补丁，严格 AOT | **零告警，退出 0** | **6 / 6，退出 0**，动态代码 false |
| 官方 NuGet／补丁版，普通动态针对性检查 | 两者通过 | 两者 **7 / 7** |

严格构建没有 `NoWarn`、关闭分析、关闭裁剪或整库保留。只有诊断 AOT 为定位运行故障显式使用 `TreatWarningsAsErrors=false`，它不参与成功判定。最终严格原生 EXE、大小、哈希及各模式指纹见[摘要](evidence/native-aot-core/summary.json)。本机 NativeAOT 成功不代替无 .NET 干净环境验收。

六项核心检查覆盖：目录／包／落盘快照／恢复四种输入的相同指纹和三次 update 结果；一次输入只消费一次；资源按会话释放；篡改快照被拒绝且不修改原内容；重复／未知 JSON 键拒绝；较窄获准目录拒绝较宽目录内容；加载失败清理；关闭期间取消并等待运行中的 update，禁止迟到提交及提前资源释放。

普通原实现、普通 JSON 候选、严格原生候选的指纹均为 `B787CFD05C2852A6086A0EB1AC0762AF259AAE7ACBE40F5E36028B79BF4878A8`。

## 公共 JSON 的最小变更

[prepare-candidate.ps1](../../tools/AotProbes/Core/prepare-candidate.ps1)按已核对字符串生成副本，源结构变化时停止。必要改动如下：

1. `ContentJson` 的读取、写出与 `SerializeToElement` 使用源生成 `JsonTypeInfo<T>`。静态目录只包含已知内容协议，未知 CLR 类型明确拒绝；保留 camelCase、未知字段拒绝、重复键检查和规范排序。
2. 为 `RuntimeStartup`、`ContentSnapshot`、`ContentIdentity`、`ContentPayload`、`ReleaseManifest` 及包清单生成元数据，`JsonElement` 继续承载格式插件的中立数据。模板为 [ContentJsonContext.cs.template](../../tools/AotProbes/Core/ContentJsonContext.cs.template)。
3. 将 `GameRuntime.Build` 的匿名 `{identity,payload}` 改为命名内部记录 `ContentFingerprintInput`。序列化 JSON 形状与排序不变，已通过上述指纹对照。
4. 将 `GamePackage.PackageManifest` 由 private 改为 internal，让同程序集源生成器可见；包格式及校验不变。
5. `PlayerPackageSource.Versions` 改为 `JsonElement`，普通导出直接读取 JSON。正式接入还需修改 `Authoring/AuthorRuntimes.cs` 和 `Runtime/PlayerDistribution.cs` 两处调用者，将已有元数据明确序列化为 JSON，不能让小核心的静态目录反射处理作者传入的任意匿名 CLR 类型。本原型不验收普通 Player 导出。

这是使用 [.NET 源生成 API](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)的有限类型闭包；并非注册 `object` 后反射所有实际类型。.NET 10 的生成器还要求相关内部类型／构造器对 context 可访问，见[源生成模式约束](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes)。

## 为什么仅靠公开静态注册不能修复官方包

实际原生错误栈为：`GameRuntime.Build → RuntimeHost.StartCore → Context.Create → EventsService..ctor → EventsService.On → JsonSerializer.Serialize<string>`。它发生在用户插件注册前；不能通过换一种插件声明绕过。[Context.Create](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Context.cs)无静态根选项，[EventsService.On](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Events/EventsService.cs)为诊断标签序列化字符串。

此外，公开 `Inject` 仍进入 `Plugin(object) → TryResolve`，其中 Type／任意 Delegate／结构化 apply 的反射分支在 ILC 看来可达；`RegisterRuntime` 是 internal。预登记元数据也不会消除 `EnsureGeneratedRegistrations` 扫描和后续类型反射。依据：[RegistryService](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Registry/RegistryService.cs)、[PluginMetadataRegistry](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Registry/PluginMetadataRegistry.cs)。

完整日志见[公共源码严格失败](evidence/native-aot-core/baseline-strict-build.log)、[JSON 修正后严格失败](evidence/native-aot-core/json-candidate-strict-build.log)、[JSON 修正后原生运行故障](evidence/native-aot-core/json-candidate-native-result.json)。可达诊断分组为：

| 诊断 | 实际归属 |
| --- | --- |
| IL2026 / IL3050 | EventsService.On、ReflectService.Provide 的字符串 JSON；PluginMetadataRegistry 扫描；Schema.FromType 的枚举构造 |
| IL2070 / IL2075 | RegistryService 的类型／属性／接口推断；PluginLoader 的构造器、注入成员、Load 方法；Schema 对象转字典；Logger 匿名配置属性；Fiber 的 `Task<T>.Result` |

## CordiSharp 候选补丁与边界

可审阅的完整差异为 [CordiSharp.patch](evidence/native-aot-core/CordiSharp.patch)，每个原文件和改后文件哈希在 [CordiSharp-changed-files.json](evidence/native-aot-core/CordiSharp-changed-files.json)。生成器为 [patch-cordisharp.py](../../tools/AotProbes/Core/patch-cordisharp.py)，只写指定新目录，不改 NuGet 缓存或正式源码。

| 文件 | 变更 |
| --- | --- |
| `Internal/StaticJson.cs`（新增） | 只为 string 提供源生成序列化，保留 JSON 转义 |
| `Events/EventsService.cs`、`ReflectService.cs` | 三类诊断标签改用静态字符串序列化 |
| `Registry/RegistryService.cs` | 仅在 NativeAOT 接受确切 object 配置的 Func／Action 静态委托，并保留原 `Inject` 与 `IPluginObject` 契约；Type、其他委托签名和结构化 apply 只在动态代码宿主走原分支；NativeAOT 对其他形状给出明确拒绝 |
| `Fiber.cs` | `Task` 和 `Task<object?>` 保留静态处理；任意 `Task<T>.Result` 反射抽到标注方法，仅动态宿主可调用；NativeAOT 的未支持返回形状明确报错 |
| `Logger/LoggerService.cs` | 字典拦截配置继续静态读取；任意对象属性反射隔离为标注方法，仅动态宿主可调用；NativeAOT 未支持配置明确报错 |

隔离方法标注 `RequiresDynamicCode` / `RequiresUnreferencedCode`，由 `RuntimeFeature.IsDynamicCodeSupported` 控制可达性。**普通 JIT 仍走原委托注册分支**，不抢占它的名称／配置推断；没有把注解静默丢弃后标记成 AOT 支持。这个补丁不宣称任意类插件、自动扫描、构造器注入、匿名配置、任意 task 结果或整个 CordiSharp 包已兼容 AOT。外部运行插件的 AOT 入口必须遵守上述静态形状。

Fiber 的动态 task 结果逻辑和 logger 的匿名拦截本来就在共享执行路径中，依据 [Fiber](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Fiber.cs)、[LoggerService](https://github.com/oniemiddle/CordiSharp/blob/7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f/src/CordiSharp/Logger/LoggerService.cs)。因此只改两个 JSON 字符串调用仍不足以严格发布。

针对性 JIT 回归覆盖类插件、结构化 apply、带 string 配置的委托、object 委托释放、`Task<IDisposable>` effect、匿名 logger 拦截和中文／引号／换行标签转义。原包与补丁均 7 / 7，见[原包结果](evidence/native-aot-core/dynamic-original-result.json)、[补丁结果](evidence/native-aot-core/dynamic-patched-result.json)。实验另外观察到 `Plugin<int>` 的配置经原 schema 转为 Int64 后无法传给 Int32；原包与补丁均如此，本次不修复该既有行为，也不据七项测试宣称完整上游回归。

正式采纳需要固定此源码版本与补丁身份、保留普通宿主回归，并复核静态入口的准入／诊断。新增 NativeAOT 拒绝是明确的支持边界；不能把这些运行期异常当作完成发布预检。公开 API 签名保持 0.2.0，但发行身份必须区分补丁实现与原 NuGet 二进制。

## 复现命令

以下目录都要求不存在；原始源码可从对应提交下载到独立 scratch。示例使用本次固定下载位置：

```powershell
pwsh -NoProfile -File tools/AotProbes/Core/prepare-candidate.ps1 -Destination .scratch/core-candidate-new
python tools/AotProbes/Core/patch-cordisharp.py .scratch/native-aot-cordisharp/source/CordiSharp-7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f .scratch/cordisharp-candidate-new
dotnet publish tools/AotProbes/Core/Core.csproj -c Release -r win-x64 --artifacts-path artifacts/core-new-build -o artifacts/core-new -p:ProbeCoreSources=C:/code/dotnet/Board-Game-Builder/.scratch/core-candidate-new -p:ProbeCordiSharpProject=C:/code/dotnet/Board-Game-Builder/.scratch/cordisharp-candidate-new/CordiSharp.Patched.csproj -p:TrimmerSingleWarn=false
artifacts/core-new/Core.AotProbe.exe artifacts/core-new-result.json
```

省略 `ProbeCordiSharpProject` 会测试官方 NuGet；再省略 `ProbeCoreSources` 则测试未修改公共源码。普通对照加 `-p:PublishAot=false`。动态回归项目为 `CordiDynamic.csproj`，可使用相同 `ProbeCordiSharpProject` 切换补丁或官方包。每次构建使用独立 `--artifacts-path`，避免增量缓存混用不同实现。

最终证据：[零告警构建](evidence/native-aot-core/patched-strict-build.log)、[原生六项结果](evidence/native-aot-core/patched-native-result.json)。本次没有修改正式程序集依赖，仍需后续集成、完整回归与干净环境验证后才能把该运行路径标为已支持。

## 干净 Windows 补充验证

2026-10-31，将摘要中固定 SHA-256 的 v4 原生 EXE 单独复制到本任务 Windows Sandbox（Windows 10.0.26100，无 dotnet 命令、无系统 .NET 运行时目录）。6 / 6 检查通过，进程退出 0；加载模块列表没有 coreclr 或 hostfxr，内容指纹与 JIT 相同。见 [干净环境结果](evidence/native-aot-core/clean-windows-result.json)。此证据仅覆盖本原型的静态组合；正式生产依赖接入和完整发行仍须验收。

为避免正式源码适配后破坏历史复现，prepare-candidate.ps1 默认从 Git 提交 b10583e 读取原公共源码，仍只写新目标目录。
