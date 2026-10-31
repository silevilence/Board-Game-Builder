# Lua 最小适配原型的严格 AOT 边界

2026-10-31。本实验消费 [原始 Lua 实证](native-aot-lua.md)发现的实际失败，单独验证「协议 JSON 源生成＋有界结果 writer＋HardSandbox 精确元数据根＋显式平台」是否足以令现有 MoonSharp 2.0.0 支持严格 NativeAOT。**结果：尚不足够，严格编译仍被第三方依赖中的 22 条分析错误阻止。** 没有更改正式 src、抑制警告、保留整个解释器程序集或替换 Lua 实现。

原型位于 [tools/AotProbes/LuaAdapted](../../tools/AotProbes/LuaAdapted/README.md)。生产 `LuaProcessSession`、`LuaProtocol`、`SeededRandom` 直接链接；LuaExecution 复制到实验目录，仅增加平台／精确根初始化，并将受限结果图改为直接 JSON writer。独立 worker 使用同一源生成协议编解码器，正常与错误响应均不再调用 Kernel Wire。构建环境仍为 SDK 10.0.112、win-x64、MoonSharp 2.0.0。

## 已验证

- C# 编译通过，未出现应用层 JSON 的 IL2026／IL3050；严格发布进入实际 ILC 后以 1 失败，没有可验收的原生运行程序。
- 普通托管构建中，同一六项真实规则、CPU、流程调用及种子 7 重复结果，与原始生产探针对照一致。直接执行和显式 worker 两种模式均退出 0。
- 指令预算拒绝无限循环；普通 worker 的请求发出前取消仍通过。这些只证明实验改动没有破坏所覆盖的普通运行，不代替 native worker 验收。
- 项目明确启用 `IlcTreatWarningsAsErrors`／`ILLinkTreatWarningsAsErrors`，保留仓库 `TreatWarningsAsErrors`，没有 `NoWarn`、`UnconditionalSuppressMessage`、整库根或关闭裁剪的配置。

完整日志在 `artifacts/native-aot/lua-adapted/`；[严格编译日志](evidence/native-aot-lua-adapted/strict.log)、[退出码](evidence/native-aot-lua-adapted/strict.exit.txt)、[直接运行](evidence/native-aot-lua-adapted/jit-direct.log)与[worker 普通对照](evidence/native-aot-lua-adapted/jit-worker.log)已存档。

## 严格编译阻碍

| 诊断 | 条数 | 固定版本内部路径 |
| --- | ---: | --- |
| IL2075 | 10 | FrameworkClrBase 的字段、方法、属性、接口、构造器、事件及嵌套类型枚举，类型流没有匹配元数据注解 |
| IL2072 | 2 | ValueTypeDefaultCtorMemberDescriptor 的动态 Activator.CreateInstance |
| IL3050 | 4 | 动态 Enum.GetValues、扩展方法 MakeGenericMethod、字典／列表 MakeGenericType |
| IL2060 | 1 | ExtensionMethodsRegistry 的动态泛型方法 |
| IL2055 | 2 | TableConversions 的动态泛型容器 |
| IL2067 | 3 | 动态字典、列表及数组实例化 |

精确保留七个 HardSandbox 模块的成员只能解决对应成员被裁剪的可用性；它不会为第三方通用 `Type` 流自动增加注解，也不会使运行时动态泛型具备 NativeAOT 代码。即使宿主从不主动登记业务 CLR 对象，现有包的解释器／互操作调用图仍把这些实现带入分析。因此不能以增加更多根或仅在主应用抑制同编号警告消除阻碍。

## 继续修复所需范围

如要继续，需选择并维护固定 MoonSharp 源码构建，而不是一个宿主配置开关。候选边界是：为只允许 Lua 数据和显式 CallbackFunction 的执行器移除／拒绝自动 CLR UserData、动态泛型扩展方法及泛型集合转换；核心模块改为静态登记或贯穿准确元数据注解；平台检测避免旧表达式树推断。涉及 Compatibility/Frameworks、Interop/UserDataRegistries、Interop/Converters、StandardDescriptors 以及其调用入口，需要证明拒绝的路径确实不属于现有 Lua 合约，再重做官方库与本项目沙箱回归。

这不是已准备好的窄补丁，也不承诺删除几段泛型代码即可达到完整兼容；移除未经分析的代码可能破坏字符串／table 内部行为。故实验在严格编译失败的可复核边界停止，没有为了生成成功产物扩大维护分支。原始版本继续标为不支持；活动超时、内存、活动取消、父进程退出与原生协议复用在严格编译通过后才有实际运行对象可测。
