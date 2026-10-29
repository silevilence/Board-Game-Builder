# 小核心与默认桌游组合验收

实施记录始于 2026-10-28，最终复核于 2026-10-29。小核心、默认桌游组合、WindowsDX、选择性普通装配、Player／Editor、作者导出及干净 Windows 发行验收已完成。各节保留当时的分项范围；后续节及末尾整体审核说明最终状态。NativeAOT、GL 和外部 .NET 运行插件不在本次完成范围。

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

## 独立、嵌入与无界面宿主

所有 PlaySession 命令与 CPU 推进切换到同一驱动后，`application-baseline` 的 21 局全部匹配原固定数据，未重录基线。Application.Tests、`session-driver board-update debug-play debug-external timeline observers batch flow-protection flow-restoration local-table` 通过；验证每局时钟隔离、暂停／单步、取消输入撤回及 Lua 调用取消后下一 tick 可继续。整解 Debug 构建与 Release Player 构建零警告、零错误。

实际运行 Debug Editor 的 `--verify-session samples/quick-cards`，使用本轮 Release Player：内嵌和外部均完成 7 条命令，覆盖脚本重载／坏脚本保留、暂停／单步、观察席位与显式作者权限、面板重建、回放、迟到结果、进程失败及重启；退出设备数为 0。实际 Release Player 的 `--verify-local` 八种热座／分屏、纯真人／混合 CPU、quick-cards／sealed-auction 组合均通过。原始结果见 [application-host-probes.json](application-host-probes.json)。这些结果不替代后续通用渲染插件、非桌游宿主与干净 Windows 发行验收。

延迟门槛仍为 1000 ms。本轮与基线及图形探针并行运行时一次达到 1185.6 ms；其他测试退出后独立复测八次为 98.7、515.4、169.5、162.9、53.0、498.8、177.2、154.0 ms，通过既有门槛。保留并行负载结果，不将其描述为独立基准。

## 独立资源加载

`dotnet run --project tests/Resources.Tests` 通过。该可执行项目只引用 Resources.Core，检查资源抽象、实现和 Application.Core 无桌游或 MonoGame 程序集依赖；非桌游内容通过插件读取单图、裁剪／旋转图集和中文字体，验证通配符依赖收集、重复名称、缺件、未知资源、越界枚举及保留入口过滤。删除源目录后，包与快照恢复仍匹配原指纹。资源提供者没有帧回调，定义提供者不解析资源格式。

`resource-font animation-runtime resource-documents atlas resource-plugins runtime-recording author-resources print` 全部通过；`application-baseline` 的 21 局与最初固定摘要一致，未重录。Application.Tests 通过，整解构建零警告、零错误。View.Tests 实际字体栅格化、UI 字形加载、重置／独立释放，以及文档授权投影、分页、刷新、终局和释放通过；此处不替代后续 WindowsDX 通用 render 接入验收。

## 渲染能力前置契约

`Rendering.Tests` 只引用 Rendering.Core，检查通用绘制及宿主程序集没有桌游、MonoGame 或 Avalonia 实现依赖。使用专用图形线程和最小后端验证缺失／重复后端、错误作用域、无界面零后端、跨线程调用、绘制重入、过期绘制上下文、帧内释放拒绝、截图转发、双视口缓存隔离和设备创建失败。真实 RuntimeHost 视口 render 调用通用绘制，关闭运行实例只释放所属视口，父后端与另一视口仍可用；最终关闭无残留，释放顺序正确。本项不宣称 WindowsDX 或 GL 适配完成。

## WindowsDX 与平台视口

`Rendering.WindowsDX.Tests` 实际运行两个原生视口：中文、单图及裁剪／旋转描述绘制与 PNG 截图、WM 鼠标／键盘／失焦输入、三次窗口缩放和重建、启动中取消、设备创建失败及恢复全部通过。检查同一窗口只有一个后端、两个独立设备和纹理缓存；移除一个视口不影响另一个，最终设备／后端／原生窗口计数均为零。截图已人工查看，原始结果及截图路径见 [windowsdx-contract.json](windowsdx-contract.json)。Avalonia 延后销毁原生子窗口，探针在有界等待后核对数量；首次探针缺少 Windows 应用清单，复用项目现有清单后正常创建。

旧 Editor `--smoke` 三轮重建通过。Player `--verify-showcase samples/poker-uno` 的 53 张牌共用一张纹理，4／5／6 人设置及对局、热座、分屏和全 CPU 观察均完成，退出无设备残留。View.Tests 的中文栅格化／UI 字体和分页文档回归通过；WindowsDX 仍明确不支持 NativeAOT。

真实 Player 探针曾停在图集关闭后的同步内容加载。最小 `Application.Tests -- dispatch-isolation` 在修复前稳定失败：图形启动／释放的同步 UI dispatch 阻塞了 CordiSharp 0.2.0 的共享 pump。改为异步等待图形 dispatch 后，最小测试、完整 Application.Tests 与原 Player 探针均通过，临时诊断日志已移除。整解重新构建零警告、零错误。

修复后 `cordi-dispatch application-baseline board-update session-driver runtime-host startup` 全部通过，21 局状态、日志、流程、投影与内容指纹仍匹配最初固定基线。

## 桌游展示插件

`BoardGame.Presentation.Tests` 验证现场／回放／远程授权投影绘制相同、隐藏牌与隐藏文字锚点不泄漏、替换展示提供者、无界面跳过展示及多帧 render 不推进权威。程序集没有 Runtime、Lua、MonoGame 或 Avalonia 实现依赖。`view board-actions freecell-shortcuts` 通过，真实 WindowsDX 双视口探针在新 render 组合下通过，最终设备、后端及原生窗口均为零。

实际 Player UNO 探针完成 4／5／6 人、热座、分屏及全 CPU 观察，53 张牌仍共用一张纹理。咒物调查员真实视口完成开局、动画、遭遇、牌桌操作、菜单、文档和缩放，截图人工查看无布局回退，退出设备为零。FreeCell seed 1 两次通关，累计 120 次双击、64 次点击和 4 次动作；每次重开终局一致。原始结果见 `board-render-probes.json`。全解构建零警告、零错误。FreeCell 标题改为显式资源的兼容要求见应用循环文档；未重录最初基线。

## 桌游交互插件

独立 `BoardGame.Interaction.Tests` 通过：重复／倒序序号、忙碌输入消费、旧版本、双连接旧帧、只读拒绝、展示无提交权限、关闭取消、重复抬起与失焦／忙碌清理。既有 `interaction freecell-shortcuts board-actions local-table` 通过，FreeCell 真实 seed 1 动作链仍完整通关。整解构建零警告、零错误。

实际 Player UNO 完成所有人数与热座／分屏，咒物调查员通过动画时输入禁止、牌桌单次提交、局部菜单取消和视口尺寸稳定。独立 Player `--verify-local` 的 quick-cards／sealed-auction、纯真人／混合 CPU、热座／分屏八种组合全部完成。结果见 [board-input-probes.json](board-input-probes.json)。联机适配仍由后续单独任务接入，原意图解析适配继续可用。

## 动画与分页通用部分

`Presentation.Tests` 在无桌游／UI 依赖的项目中验证插值、时间边界、7／16／33／100 ms 取样、页码和面板切换。`Presentation.Avalonia.Tests` 仅引用通用页面库，实际建立 Avalonia 页面，验证文本排版、刷新保页、结构替换拒绝及释放，并核对程序集无 Kernel／Runtime／View.Abstractions 引用。View.Tests 的旧授权字段、终局阅读、字体和清理测试继续通过。

`animation-runtime resource-documents animation-author` 验证旧格式、隐藏实体与属性、触发重置、最终姿态、包和作者草稿。实际咒物调查员 Player 的发牌、牌桌、日志分页和参考窗口通过；Editor 的运行文档探针及动画预览两轮启停、保存重开、失败和切换清理通过。动画探针首次在测试副本保存 animations.json 时返回 Windows“无法删除要被替换的文件”；未发现只读属性，简单替换测试正常，相同探针原样重试通过。未更改文件属性／权限，也未改作者写入算法；首次失败保留在原始结果中，不能据此宣称从未发生保存错误。结果见 [presentation-probes.json](presentation-probes.json)。最终整解构建零警告、零错误。

## 可选普通装配前置

三个独立可执行测试分别只引用 Application.Core、BoardGame.Hosting、BoardGame.Hosting + BoardGame.Lua，通过正式加载、format 3 打包、驱动和关闭。桌游两种组合完成终局及记录校验回放；Lua 组合使用实际受限 worker。测试核对 deps.json：非桌游没有 Kernel／桌游实现，无脚本桌游没有 Lua／WASM／图形实现，Lua 桌游没有 Wasmtime 或兼容 Runtime。实际项目引用清单见 [optional-assembly.json](optional-assembly.json)。同时验证未选工厂不绑定、外来获准目录拒绝和必需记录能力缺失拒绝。

抽离后的默认 Runtime 在最初 21 局基线上逐项匹配，未重录；`board-recording board-update session-driver lua-executor wasm-executor runtime-recording` 全部通过。后续 `packages content-discovery online-reconnect online-audience online-secrets runtime-host startup` 全部通过。完整构建零警告、零错误。一次并行 NuGet 还原报告“文件已存在”；使用正常串行还原后通过，没有删除缓存或修改安全设置。上述装配验证不宣称 Player 外壳、普通发行 UI 或干净 Windows 验收已完成，这些仍按后续任务独立验收。

追加 WASM-only 宿主验证：只引用 BoardGame.Hosting + BoardGame.Wasm，实际扩展计数命令、更新发布、记录回放及加载时的 WASM 内容哈希拒绝通过，deps.json 不含 Lua 或兼容 Runtime。载荷加载器通过新增的默认接口重载接收独立复制的有效启动配置；旧加载器继续使用原 Load(source)，保留禁止将 game.start.json 当普通内容文件读取的边界。

## 非桌游实时示例

Motion.Tests 通过正式目录／包加载、固定输入和 10／20／50／100 ms 调度一致性、目标反馈、暂停／单步、120 帧绘制不更新状态及真实 pump。deps.json 检查无桌游／Lua／WASM 实现。Motion.ViewTests 实际打开独立与双嵌入视口，经原生按键驱动连续运动；暂停仍绘制，关闭一个视口保留另一视口输入，失焦取消只影响自身。通用 ApplicationWindow 重复启停及加载中关闭通过，最终设备／后端／原生窗口计数全零。截图已查看，标题、目标、标记和反馈均正确。

全解构建零警告零错误，WindowsDX 原有双视口契约和 FreeCell 完整快捷键通关回归通过。证据见 [motion-probes.json](motion-probes.json)。此时普通 Player 的统一能力入口仍由下一任务验收，最小宿主不替代该任务。

## Player 外壳与本地席位

Player.Hosting.Tests 在同一获准目录依次打开桌游、实时目录、实时包及再次桌游，实际输入推进实时实例，确认没有席位控件，逐次退出设备归零；缺提供者、冲突提供者和未显示窗口取消通过。通用宿主程序集引用检查无桌游、Runtime 或示例实现。

真实 Player 的旧热座／分屏探针覆盖 quick-cards 与 sealed-auction、纯真人与混合 CPU 八种组合；外壳探针覆盖 freecell／island-settlers／quick-cards × Tools／Game 六种组合，以及取消设置、非法配置、混合席位、重开、经典牌桌热座、包内帮助和加载失败恢复。初次固定 80／160 ms 等待未获得首帧，串行复现后改为最多 5 秒等待明确首帧或绘制错误，所有组合通过；没有放宽规则、终局或资源断言。原始失败与通过结果均保存于 [player-host-probes.json](player-host-probes.json)。

## Editor 通用试玩与桌游调试

实际 Editor 应用探针在嵌入和独立 Player 两种模式加载未保存 Motion 草稿，核对来源文本与磁盘字节不变、暂停／固定单步／继续、两次面板重建保留同一实例与 tick、外部进程死亡清除状态、切换文档时丢弃迟到启动、切回桌游和运行提供者停用／再启用。最终图形设备与后端为零。外部握手等待真实视口首帧。

原桌游 SessionVerification 的嵌入／外部两种模式通过：席位观察、显式作者真值、Lua／CPU 重载、坏脚本保留旧状态、三次面板重建、非法动作、忙碌暂停、终局记录与回放、停止迟到响应以及外部进程故障；两种模式均记录 7 次命令，最终设备零。真实 foundation 与 runtime-documents 探针通过；无界面 cordi、cordi-dispatch、flow-editor-lifetime 以及 Application.Tests 全部通过。全解构建零警告零错误。证据见 [editor-session-probes.json](editor-session-probes.json)。

## 作者能力与 CLI 发现

Motion.Authoring.Tests 通过独立提供者的格式读写、文档发现、运行校验、60 次固定更新，以及磁盘／GUI 两种模式下的来源版本、旧版本拒绝、不隐式保存、Undo／Redo、显式保存、旧工作区代次拒绝和缺提供者诊断。真实 CLI 对 motion-lab 完成五项实际检查（含 initial-application-update），无诊断。

真实 Editor—CLI 连接探针在原桌游工作流之外，追加实时工程：无专用 Editor 格式仍保留文件编辑，CLI 配置写入仅修改草稿；校验、12 次固定更新及嵌入试玩读取同一草稿版本，Undo／Redo 和显式保存通过。原连接的导入撤销、未保存打包、外部冲突协调及三次连接重启继续通过，最终插件 effects 为零。foundation、runtime-documents 实际探针通过；author-contract、author-profiles、author-operations、author-validation、author-docs、author-workspace、author-cli、author-templates、author-connections、author-connection-lifetime 和 documents 回归通过。全解构建零警告零错误。

CLI 首次运行发现本地输出目录中 2026-10-10 的 Author.SamplePlugin.dll 仍依赖旧 Authoring 1.0.0.0；保留副本到 .scratch/application-loop/Author.SamplePlugin.before-author-refactor.dll 后，以当前样例工程重建产物替换该开发输出，两文件 SHA-256 一致。没有放宽插件版本检查或增加加载回退。证据见 [author-capability-probes.json](author-capability-probes.json)。按用户后续要求，本条提交后停止；后续结构化面板、导入打印、联网、模板迁移、普通导出及整体验收尚未完成，不勾选。

## 结构化面板与流程作者工具（2026-10-29）

桌游模型面板现在要求作者格式显式声明 `board.author-model`；通用配置、YAML 和文件编辑仍接受其他作者格式。FormatPanel 按文档代次、格式目录版本及定义路径重建子上下文，旧子上下文在异步清理前就停止读取新文档。规则／CPU 编辑也进入格式子上下文；流程、动画继续使用已有共享领域操作及版本化工作区。

实际 Editor 的 foundation、runtime-documents、flow 和 animation 探针全部通过。覆盖无效字段和面板重建、非桌游结构化格式、同格式文档替换时旧通知隔离、流程连线／属性／作者轨迹、真人／CPU 与全 CPU 终局、动画撤销／保存重开及两次预览释放。动画探针耗时较长但在原 180 秒限额内完成，未增加超时或修改断言；截图已检查。原始结果见 [structured-panel-probes.json](structured-panel-probes.json)。

documents、animation-author、七项 flow-editor 回归及 review-followup 通过。旧 review-followup 固定插件总数未跟随后续能力增长，改为检查必需兼容身份；同时恢复独立状态解析器在结构错误中的 `$.state` 路径，保留原诊断断言。Editor 构建零警告零错误。导入／打印和后续任务尚未验收。

## 共享导入与打印作者插件（2026-10-29）

图片切分、TTS 转换和 PDF 拼版算法迁到 Authoring，程序集没有 Editor 或 Avalonia 引用。GUI 从含文件草稿的 ProjectView 创建预览，经 WorkspaceEditMode 提交定义、文件及启动资源声明；CLI 独立作者插件复用相同实现。旧 ModuleDocument 导入接口仅保留显式兼容适配。

board-author-media 覆盖 GUI 未保存定义、整组 Undo／Redo、磁盘不变、预览版本冲突、旧文档代次拒绝、无格式支持拒绝，以及独立作者宿主的 TTS 导入和 PDF 输出；GUI 与磁盘拼版每张图像字节一致。image-import、tts-import、print、atlas、author-resources、author-profiles、author-operations、author-cli、author-docs 回归通过。

实际 Editor M3 探针完成图片、TTS、打印各三轮重载／停用，字段改变后的旧预览拒绝、一次撤销、三个 PDF 输出及独立 WASM 运行均通过；拼版截图已查看。结果见 [author-media-probes.json](author-media-probes.json)。Editor 和 Engine.Tests 构建零警告零错误。

## 联机与观战能力接入（2026-10-29）

OnlinePlayerSession 经 board.interaction 和绑定连接身份的提交目标发送命令，保持连接门、服务器序号及恢复时序；客户端输入根不包含权威或 update 贡献。公共观战不装配提交能力。现场和身份回放都能提供复制隔离的 ISeatProjection，图形窗口消费该投影；服务器继续经唯一 PlaySession 驱动推进，deps.json 不含展示或 Avalonia，Runtime 不引用 Networking。

online-capabilities 验证流程卡牌逐步断线／恢复、每席位反复读取和篡改返回投影仍不推进流程或改写原记录，最终身份回放成功。全部既有 online-identity、identity-persistence、ordering、reconnect、audience、secrets、http、processes、faults、review、client-review、followup、cursor 及 local-table 通过；UNO 4／5／6 人均完成混合 CPU、两个真人、观战与两种恢复。

真实 Release Player 的两个真人视口与观战窗口完成 quick-cards、本人回合断线恢复和身份记录回放，三个视口均有实际帧。原始结果见 [network-capability-probes.json](network-capability-probes.json)。首次自制 GUI 探针配置误加 p3，被服务器正确拒绝；按实际两个席位纠正配置后通过，未更改服务器校验。Server、NetworkClient、Player 的 Release 构建零警告零错误。

## 独立导出提供者契约（2026-10-29）

AuthorExports 在 author.core 中提供按作用域登记的无 UI 导出目录，描述身份、版本、平台、依赖、产物、参数、预检和执行；请求携带固定 ProjectView、取消与进度，结果核对来源及产物类型。注册仍归 CordiSharp 效果，活动执行阻止撤销其提供者，没有按类型回退。

author-export-contract 用两种独立提供者验证共同发现、重名拒绝、同时执行、活动撤销保护、停用／重启无重复、取消及来源一致；author-profiles 通过。此契约是 CLI 与 GUI 共用的类型和目录，真实内置提供者、UI 贡献及实际包输出由后续任务验证。

### 独立游戏数据包提供者（2026-10-29）
- author.export.package 独立提供 project.pack，显式 Includes 保留旧 author.export 调用；运行时通过 IAuthorPackageRuntime 选择正式格式 2／3，不在通用入口解析桌游状态。
- author-exports、author-profiles、author-export-contract、author-docs 通过；Motion.Authoring.Tests 验证磁盘与 GUI 未保存草稿包、匹配目录加载、无引擎二进制和源文件不变。桌游 module／state 的仅数据包 profile 与停用检查通过。
- 首轮暂存文件缺 .bgm 后缀导致旧加载器按 JSON 读取，已修正暂存扩展名并重跑；没有改动包协议或放松加载检查。

### 独立预编译 Player 提供者（2026-10-29）
- author.export.player 独立提供 player.export，旧 author.export 显式引用两个普通提供者；默认 profile 改为分别启用。PrecompiledPlayer 只负责清单、文件、托管依赖和图形入口，内容协议由作者运行时提供。
- 实际构建 artifacts/application-loop-player 与 artifacts/application-loop-minimal-player-verified。export、export-large-package、export-linked-root、export-dependencies、flow-delivery、author-exports 通过；包含缺依赖、目录链接、篡改、取消及大包验证。仅 Player profile 无需包提供者。
- Motion.Authoring.Tests 从非桌游内容导出；目录无 BoardGame.*、Kernel、Runtime、Modules、Lua、WASM 实现。Play.exe 从临时工作目录启动，真实 HWND 键盘输入两次到达目标，绘制后关闭，设备与原生窗口均归零；证据 .scratch/application-loop/minimal-standalone-export.json。干净 Windows 属最终发行回归，尚未标记完成。

### Editor 提供者发现与独立贡献（2026-10-29）
- Editor 零警告构建；真实 --verify-export 通过 14 项检查，记录 docs/validation/export-provider-probes.json。分别仅启用 Player／数据包、同时启用、三个启停周期、命令移除、CLI 描述一致、缺插件保留配置均通过。
- 两种产物来自同一未保存草稿，源文件未变。活动导出拒绝停用，附加步骤受保护；切换文档取消且没有迟到状态串用，强制关闭等待取消并释放提供者。快照文件占用时产物有效、警告明确且状态恢复。
- 已检查实际导出面板截图 .scratch/application-loop/export-providers-afb210704791457ea9e589f31abbea43/providers.png。

### 示例与模板显式组合（2026-10-29）
- git 跟踪的全部 12 个运行入口已盘点：11 个桌游显式声明 board.render，1 个实时入口不变；原入口与换行归一后的新文本 SHA-256 保存于 application-entry-migration.json。历史证据中的录制／快照保留原身份，不改写。
- GameAuthorFormats 共用模板、runtime_startup.py、build-poker-uno.py 以及 TestModules／FlowTests 图形发行夹具已同步。纯无界面隔离测试保留其明确的无展示组合。迁移脚本仅在作者显式调用时修改指定目录，重复执行／检查不改内容。
- application-migration 验证所有桌游目录、包、作者校验；author-templates 验证 module／state 创建与完成对局；真实 Editor runtime-documents 验证新模板与替代格式试玩；Motion.Authoring.Tests 通过。原 application-baseline.json 未改，21 场原身份完整基线匹配。
- 图形 Player／普通导出要求匹配的显式展示声明；无界面主机只验证元数据，不加载绘制实现。旧记录仍配套原身份恢复，图形迁移说明见 application-compatibility.md。扩展 20 种子语义比较已加入独立 application-regression，作为下一项验收执行。

## 最终语义与生命周期回归（2026-10-29）

`application-regression` 全部 72 组新旧对照通过：quick-cards／hex-skirmish／island-settlers 各种子 1～20，拍卖／UNO／流程／WASM 各种子 1、7、42。每组验证完整录制链及回放，再比较提交、效果、权威状态、逐步各席位投影、流程及步数。原 21 组身份基线继续匹配，未改期望值。原始结果见 [application-semantic-regression.json](application-semantic-regression.json)。

FreeCell 20 次发牌／回放、既有 seed 1 的 94 步合法通关及快捷键通过；嵌入、外部 Player、交互适配和回放结果一致。非法命令、可见性、秘密提交、事务、Lua／CPU／重载／worker 超限、WASM 隔离、流程保护与恢复、update／session-driver／记录／联机投影回归通过。修正两处调度测试的观察视口夹具，显式替换新模板的 board.render；不向无 GPU 测试补装真实设备，也未削弱生产图形依赖检查。外部模式首次指向旧 Release Player，重建依赖后通过。

通用及选择性程序集的架构测试全部通过。实际 Motion.ViewTests 复测一个独立窗口、两个嵌入视口、持续键盘输入、失焦取消、暂停绘制、单步、关闭其中一个视口、重复启动和加载中关闭；WindowsDX.Tests 复测中文绘制、纹理、输入、尺寸变化及失败清理。最终 devices／backends／nativeWindows 全部为零；Presentation.Avalonia.Tests 的分页、替换拒绝及释放通过。证据见 [application-lifecycle-regression.json](application-lifecycle-regression.json)。

同机 Release 性能门槛仍为 1000 ms。首次最终测量有一次 1376.6 ms，Lua CPU 调用峰值 1211.9 ms（包含进程启动）；保留失败，未声称已证明其外部原因，也未修改实现或门槛。检查后连续三轮独立执行、六个新对局共 24 次操作全部通过，最大 408.9 ms。干净 Sandbox 图形首轮耗时较高，属于发行兼容验证，不充当同机性能数据；冷启动仍有环境波动风险。

## 最终作者与普通发行回归（2026-10-29）

前述真实 Editor／CLI 创建、文件及结构化编辑、图片／TTS 导入、流程画布、动画／文档、Undo、草稿试玩、外部 Player、打印及独立两种导出结果共同覆盖作者链路；操作使用捕获的 ProjectView，源工程没有被校验或导出隐式保存。全解 Release 构建零警告、零错误。Launcher.Tests 复查内容清单、共享组件、并发取消、导入／事务恢复、修复卸载、作者版本选择绑定及固定版本技能共存，全部通过。

以 `03bcca1` 的正式 Player、最小 Player 和自包含作者工具，在两个新建的 Windows Sandbox 中执行 [validate-application-loop-windows.ps1](../../scripts/validate-application-loop-windows.ps1)。网络关闭；两个环境均为 Windows 10.0.26100，dotnet 命令及 x64／x86 系统 .NET 路径均不存在。第一轮 Editor 实际完成图片导入撤销、创建编辑保存重开、嵌入及外部对局和正式导出；CLI 分别校验、打包、导出 quick-cards 和 motion-lab。非桌游产物枚举确认没有桌游、Lua 或 WASM 二进制。

第二轮只映射第一轮成品和证据目录，没有源码及作者安装。完整目录移动至带空格和中文的新路径，原位置不存在；quick-cards 图形操作完成两局，motion-lab 两次原生键盘输入到达目标并输出实际绘制截图，最终设备／原生窗口归零。两个 GUI 进程成功退出，全程每 50 ms 的可见控制台枚举未发现新控制台；预编译清单同时校验 GUI PE 子系统。结果及各项原始回执见 [application-clean-windows.json](application-clean-windows.json)。

开始时系统只允许一个 Sandbox，经用户明确授权关闭既有实例后重新创建；新环境默认 Restricted 导致首次脚本未执行，另经明确授权仅两个验收 PowerShell 进程使用 RemoteSigned。没有修改宿主或持久策略。两轮结束由脚本正常关闭各自 Sandbox。没有把隐藏 PATH 的本机运行当作干净环境，也没有用 AOT 研究替代普通发行验证。

## 整组审核与完成结论（2026-10-29）

按 ROADMAP 逐项复核 28 个任务及必要前置：小核心不引用桌游或 UI；初始化依赖与回调顺序分离；单实例单驱动、多视口只读；准备／提交、取消／迟到结果与三类作用域清理有对应测试。桌游权威、随机、秘密信息、规则和记录约束继续由桌游组合承担。核查通用内容与桌游包的显式入口、模板和迁移清单，没有恢复静默旧格式解析或游戏专用宿主分支。

作者端核查 CordiSharp 所属贡献、格式能力／文档代次、ProjectView 来源、草稿与 Undo、独立导出注册及活动租约；发行端核查正式 GUI 清单、文件哈希、依赖闭包、内容指纹、暂存发布与取消。原基线文件和初始设计历史文档无差异。更新的本地文档链接全部有效，作者文档随 CLI 内容复制／Authoring 嵌入资源发布，`author-docs`、`author-templates`、`application-migration` 复测通过。

最后一轮 `packages export-dependencies runtime-host startup definition-plugins runtime-recording runtime-entry alternative-definition` 全部通过；CursedInvestigators.Tests 的九组真实规则用例通过，View.Tests 的中文字体、授权分页、刷新和释放通过。`dotnet build BoardGameBuilder.slnx -c Release --no-restore` 零警告、零错误；更新后的 `scripts/check-boundaries.ps1` 对全部 src 项目执行明确依赖白名单且拒绝未声明项目，检查通过。没有遗留本任务的 Lua／Player／测试进程，Sandbox 列表为空。

结论：本组可标记完成，无剩余代码或验收阻塞。本地提交，未推送。首次性能冷启动超预算已保留，其后连续三轮通过既有门槛；不保证任意环境冷启动耗时。WindowsDX 普通发行的结论不外推至 GL、NativeAOT、跨系统发布、任意外部 .NET 插件或实时联机。未来这些扩展继续按独立 ROADMAP 条目实施。
