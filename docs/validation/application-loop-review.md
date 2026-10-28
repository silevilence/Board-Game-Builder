# 小核心阶段调度验收

日期：2026-10-28。记录阶段一各项的独立验收；桌游 update/render、Player／Editor 迁移和整组联合审核尚未完成。

- 迁移前基线与兼容矩阵见 [application-compatibility.md](application-compatibility.md)。七个示例、各三个固定种子共 21 局，提取宿主后再次运行 `application-baseline` 全部匹配原摘要。
- `dotnet run --project tests/Application.Tests` 通过。该项目只引用 Application.Core，检查公共程序集不依赖 Kernel、Kernel.Abstractions、Editor 或 MonoGame。包含显式阶段顺序、独立依赖图、顺序环、跨阶段／作用域错误、纯接口提供者、可变／固定逻辑时间、追赶限额、暂停／单步、输入复制与去重、两视口、绘制故障隔离、准备失败与提交故障的不同展示策略、慢任务关闭后的迟到结果、重复启停、启动失败清理、子作用域初始化中关闭及设备／实例／视口逆序释放。
- `Engine.Tests runtime-host cordi-dispatch startup runtime-entry runtime-recording` 通过，保留 CordiSharp 服务、资源与 UI 同步上下文隔离回归。
- `dotnet build BoardGameBuilder.slnx --no-restore` 通过，零警告、零错误。原 RuntimeHost／Scope／Definition 由 Runtime 程序集转发到 Application.Core，启动身份类型由 Kernel.Abstractions 转发到 Application.Abstractions，既有源代码无需改名。

调度器不代替插件自己的事务：准备阶段必须保持可见状态不变，返回的提交动作必须原子且不抛异常；违反提交契约会停止推进和绘制。StopAsync 等待不合作的回调退出，不承诺强制终止任意 .NET 代码。真实图形线程、桌游循环接入、不同帧率下实际游戏交互及干净发行环境仍需后续任务验收，以上测试不替代它们。

## 启动校验与内容发现

提供者通过 `PluginContentContract` 声明配置字段、纯校验、组合必需能力、准备作用域、定义来源／预设及额外文件。`RuntimeDiscovery` 不识别具体插件 ID；桌游必需能力由桌游定义提供者声明。配置、预设及内容发现不激活规则、CPU 或流程执行器。准备阶段依赖不能指向玩法服务。

`content-discovery` 添加只存在于测试获准目录的定义提供者，通过 `GameRuntime.Load` 的目录、入口路径、包和快照入口验证，无需修改宿主目录分支；该配置只有一个定义插件，不被自动补装桌游规则或席位服务。此项仍使用既有 GameDefinition 载荷，非桌游载荷留给下一任务。未知实现、版本、未知字段与路径越界均拒绝。

`content-discovery startup definition-plugins alternative-definition runtime-entry resource-plugins flow-delivery wasm-extensions` 八组回归通过。宿主可显式传入 `approvedPlugins`，它是受信任发行目录参数，不从游戏内容加载程序集；调用方恢复自定义快照时必须提供同一获准实现。默认内置目录、ID、有效配置和内容身份保持不变。

## 通用内容载荷与打包

`tests/Application.Tests/ContentTests.cs` 的 motion 内容完全不引用 GameState、席位或桌游契约：使用自身格式、两个预设及各自资源，从目录、固定入口和 format 3 包实际加载；删除原目录后分别从包和快照恢复。只在构建时序列化初始内容，更新使用普通 double 内存状态。测试拒绝未知载荷／封套版本、内容篡改、漏掉未选中预设资源、未知提供者、路径越界、大小写重复路径和额外未声明内容，并确认打包／恢复没有激活运行插件。该用例是内容契约验收，不代替后续真实图形实时示例。

桌游 format 2 包、快照／记录仍由显式适配保留旧身份和结构校验。`packages runtime-recording atlas resource-font animation-runtime resource-documents` 六组回归通过；基线对局结果保持原值。通用核心的打包与恢复没有调用桌游状态校验。

## 独立桌游基础能力

`board-kernel-plugin` 验证不依赖 Runtime／Lua／Wasmtime 的独立内核插件装配、投影包装不可转换为权威接口、绑定席位投影的复制隔离、非法命令不改变状态，以及普通规则声明受保护依赖仍被启动校验拒绝。安装目录权限与游戏配置分离，配置内伪造权限不能授权。记录、流程与会话中可取得权威信息的接口也受相同限制。

`board-kernel-plugin runtime-host startup commands visibility secrets flow-runtime runtime-recording` 八组回归通过。Kernel 算法、事务与受控随机未拆散；既有基线命令／投影／记录继续逐项比较。该任务不表示桌游 update 或展示插件迁移已经完成。

## Lua 执行器与调用绑定

`lua-executor lua cpu sandbox worker reload flow-systems flow-restoration` 八组回归通过。新用例用位置／速度／时间数据调用独立执行器，验证自定义数组字段、无桌游字段猜测、函数导出校验、惰性 worker、多实例与多次调用的 VM 隔离、未知 IPC 版本拒绝，并检查执行器程序集不依赖 Kernel、Runtime 或 BoardGame.Core。既有 worker 测试继续验证超时／内存超限、回收、父进程退出、重复释放及并发响应隔离；重载失败保留旧脚本。

## WASM 执行器与效果绑定

`wasm-executor` 验证不依赖桌游程序集的独立算术调用、每次全局状态重置、过期句柄拒绝、导出／imports／重复模块／体积预算及重复释放。既有 `wasm wasm-extensions wasm-isolation` 覆盖四种能力、越权与非法效果、内存／fuel 超限及权威事务原子性。

本机原实现和提取后实现均在越界读取用例以 `0xC0000409` 终止；替回 HEAD 原实现的对照实验确认此为迁移前问题。[上游问题 374](https://github.com/bytecodealliance/wasmtime-dotnet/issues/374) 与 [PR 390](https://github.com/bytecodealliance/wasmtime-dotnet/pull/390) 说明 CET 与信号式 trap 恢复冲突。启用显式检查后，越界读取、unreachable、整数除零及递归栈溢出均返回预期 TrapException，原隔离用例完整通过；未关闭 CET 或修改系统策略。兼容层依赖固定绑定的内部配置句柄，正式公开 API 发布后需移除；AOT 支持另行实证。

## 流程与桌游推进协作

`flow-progression` 只装配内核、流程与测试观察视口，不装配 session、记录或 CPU。通过公开推进契约完成动态席位多次动作及终局，验证空 update 不进入节点、等待时重复推进／投影／render／重建观察视口不增加日志、过期动作拒绝及终局不重复效果。展示只接收绑定席位投影，系统上下文不泄漏。此处使用测试 render 回调，不替代真实图形验收。

`flow-runtime flow-progression flow-systems flow-players flow-protection flow-restoration flow-review` 七组通过，覆盖分支优先级、原子失败、秘密信息、取消／预算、等待与已提交边界恢复及规则互斥。对等待动作校验边界调整后再次运行 progression／players／protection。

## 桌游记录插件与只读回放

`board-recording runtime-recording replay flow-restoration secrets reload` 六组通过。新增用例分别完成 Lua、流程和 WASM 示例后关闭运行实例，再独立创建每席位观察投影，与实时最终投影逐项比较；重复读取和修改返回值不改变回放内容，返回对象不能转换为提交／会话／权威接口。测试未知席位、坏校验和、缺失结构与玩家投影记录误用。现有回归继续覆盖脚本切换、实际随机结果、流程路径、秘密提交及旧日志拒绝语义。

## 桌游 update 与候选发布

`board-update` 将 quick-cards、flow-cards、wasm-counter、sealed-auction 在每秒 20／120 次逻辑调用下跑至终局，与原同步提交的完整权威日志和状态比较一致。双观察视口每步各绘制八次不增加命令。验证等待真人空转、快速重复输入／过期版本、慢 CPU 提供者期间旧投影可读、并发直接推进拒绝、流程预算暂停续行及 update 之间的脚本重载／回放。

后续 update 阶段分别注入取消和失败，确认桌游候选已算完但尚未发布时仍可绘制旧状态，失败后权威记录逐字不变。独立内核用例覆盖历史分支隔离、重复发布、旧版本拒绝及发布后保留句柄失效。Application.Tests 增加发布首动作触发取消的用例，确认不会留下半帧。`board-kernel-plugin runtime-recording flow-progression flow-protection flow-restoration runtime-host reload` 回归通过；真实宿主、实际图形与整组性能验收仍待后续任务。
