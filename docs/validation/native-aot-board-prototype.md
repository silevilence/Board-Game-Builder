# 最小无脚本桌游的公共 NativeAOT 链原型

2026-10-31。对应 ROADMAP「适配公共运行链路的 NativeAOT 与裁剪」。隔离原型已完成 **strict NativeAOT 发布，零警告，并在本机完成固定种子对局、记录恢复与中途恢复后续执行**；结果与当前正式项目的普通 JIT 运行逐字一致。该结果证明最小无脚本链可适配，不代表正式多程序集运行库、全部桌游能力或完整原生发行已交付。

## 实际运行与证据

入口为 [tools/AotProbes/Board](../../tools/AotProbes/Board/README.md)。固定 SDK 10.0.112、win-x64；候选引用 Core 实证的 CordiSharp 静态适配 DLL，来源及限制见 [Core 报告](native-aot-core-prototype.md)。没有加入 Lua、WASM、图形后端、Editor 或 YamlDotNet 引用。

同一个 Program 分别执行：普通版本引用现有 `BoardGame.Hosting` 项目及其真实依赖；AOT 候选编译隔离复制的 Application、Resources、Kernel 与 BoardGame 源码及必要的 Modules 运行时原语。实际调用仍是生产 `BoardApplication`、`ApplicationCatalog`、`GameRuntime.Load`、`GamePackage.Pack`、`BoardSessionPlugin`、Kernel 与 `SessionRecording`，未另造游戏规则或模拟录制结果。

覆盖行为：

- 创建带一席位、牌堆及三张牌的无脚本数据工程，经正式加载、打包、再次加载，内容指纹一致。
- 种子 42，经唯一 update 驱动顺序提交 shuffle、draw、finish；两次新会话得到相同动作与结果，抽到 `a`。
- 捕获完整 SessionRecording，序列化后重新读取并验证版本内容、hash 和 replay；权威状态和席位投影与在线会话一致。
- 在 draw 后另取录制检查点，重放恢复后继续 finish，状态与未中断会话一致。
- 修改记录 hash 后重放被拒绝；大小写实体键 `A`、`a` 的 ordinal 排序和反序列化后的 comparer 保持一致。

普通与原生两次运行的记录 SHA-256 均为 `F20FBCCFEE852B7DD434504CC70E41032A0635562ED08D2574E22D8223FCD948`；最终状态 hash 为 `F92CB9B61DA77D007C4936626AB7D433AA5457A57C520525986FAD8021A7F4A6`。原生发布不附带 coreclr、hostfxr、托管 CordiSharp DLL 或脚本执行器。

完整复现证据保存在 `artifacts/native-aot/board/verified/`，包括独立 baseline/native 构建目录、源快照、构建／运行日志与退出码、环境、CordiSharp DLL hash 和原生产物清单。精简证据见 [evidence/native-aot-board](evidence/native-aot-board/)。本报告首先记录本机实证；干净 Windows 结果必须另行记录，不能由“只有 exe”推定。

## 必需的最小适配

1. **公共通用内容 JSON。** 使用主线程已加入 Application.Core 的源生成契约、命名 fingerprint 输入及 JsonElement 作者版本字段；不是关闭反射错误后继续使用泛型反射序列化。
2. **Wire 静态上下文。** 用 JsonTypeInfo 重载实现 Json／Read／Copy；显式登记 GameState、LoadedModule、命令／动作／投影、录制、BoardUpdateRequest、BoardApplication.Payload 等根。未登记的动态 CLR 类型不静默回退反射。
3. **闭合 ordinal 转换器。** 删除 `MakeGenericType` 和 `Activator.CreateInstance` 工厂，显式提供 string、int、int?、byte[]、JsonElement、Entity、CounterBounds 七种值类型的 SortedDictionary 转换器，保持重复键拒绝、ordinal comparer 和排序写入。转换器内部递归调用生成的 `JsonTypeInfo<T>`。
4. **稳定命名的身份输入。** 将 ModuleSnapshot 指纹、Replay header／entry hash、FlowState 上下文大小检查及历史录制校验中的匿名对象换成同字段顺序／命名的内部 record。实际现代录制 hash 与正式代码一致；历史 format 1 尚未单独验收。
5. **直接 JSON 调用。** BoardStateDocument、BoardUpdate、BoardApplication.Payload、RuntimeDefinition 的序列化／反序列化改用显式类型信息；空 render 配置使用 JsonElement，不创建匿名 CLR 对象。
6. **模块运行／作者边界。** 此最小链仅需 ModuleSnapshot、RuntimeDependency 数据及 SHA-256 原语。候选从 ModulePackage 精确提取后两者，没有编译历史模块目录加载、历史打包或作者 YAML。正式适配应将这些可复用原语迁入职责明确的运行程序集，保留原来的显式历史／作者 API，不在原生加载中增加回退。

## 正式集成边界

实验把多个源程序集临时编译为一个可执行程序集，目的是快速验证调用路径与必要静态元数据；**不可将 WireStatic 的顶层上下文直接放入 Kernel.Abstractions**，否则会反向依赖 BoardGame.Hosting／Session／Recording。正式版本应按所有者分配上下文：Kernel 自有状态／命令／Replay，Modules 自有指纹契约，Recording 自有录制，Hosting 自有 payload；跨层复制／编解码显式传入类型信息或经静态获准的编码注册契约，不通过全量静态目录重新耦合可选实现。

CordiSharp 候选仍是研究版固定源码补丁，需要进入受审查的依赖管理路径。它只承诺 Core 报告中已经实测的静态配置／回调入口，不能据本次桌游结果声明任意动态插件入口支持 AOT。

此次无脚本组合未验证流程图、秘密提交、资源动画／文档、旧录制 format 1、不同规则提供者、CPU、Lua、WASM、显示／输入或新外部插件。静态 JSON 的根集合必须随支持组合显式扩展并分别回归。当前正式 `Wire`、跨程序集上下文及模块分层尚未改变，因此不据本原型勾选公共运行链的正式实现任务；它提供的是可复核的成功路线与精确修改范围。

## 干净 Windows 补充验证

2026-10-31，在本任务 Windows Sandbox（Windows 10.0.26100，无系统 .NET）单独运行最终原生 EXE，SHA-256 为 `62F43B6730BF236EB38C592D3DA5C458721E6208FA8399DCE26723211DED2FBF`。退出 0，两次固定种子结果、recording hash 和 state hash 与 JIT 逐字相同；完整恢复、检查点继续执行和篡改拒绝均通过。见 [干净环境输出](evidence/native-aot-board/clean-windows-result.log)。没有将源码、SDK、托管实现或游戏输入复制到该探针目录。
