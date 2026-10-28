# 小核心执行契约（版本 1）

`Application.Abstractions` 只包含启动身份及阶段、时钟、输入契约；`Application.Core` 依赖它与固定 CordiSharp 0.2.0。二者不依赖桌游内核、Editor 或图形后端。已有 `RuntimeStartup`／`RuntimePluginSelection` 保持 JSON、命名空间及字段，旧程序集通过类型转发保持引用兼容。通用启动不表示桌游运行门面已经迁移。

## 生命周期与作用域

图形宿主拥有设备能力，运行实例拥有模拟与唯一驱动，视口拥有绘制资源。每个作用域通过独立 CordiSharp 根装配，依赖只能来自本作用域或显式父作用域；实例可以依赖图形宿主，视口可以依赖实例，反向依赖拒绝。停止实例先取消并等待其 update，再关闭视口，再逆序释放运行插件；不释放独立的图形宿主。关闭一个视口不影响其他视口或模拟。

插件可以只提供接口、不提供回调。`Provides`／`Requires` 表示唯一服务能力，`PhaseContribution` 表示多贡献集合；update 和 render 不是唯一服务键。初始化按能力图，帧内执行按独立的 Before／After 图；边指向 `plugin/contribution`，必须存在且同阶段同作用域。未约束的贡献按稳定 ID 排序，注册和订阅顺序不影响结果。缺能力、重复唯一能力、循环和非法阶段在激活前失败。

插件声明 Headless／Graphical 模式与 GraphicsHost／Instance／Viewport 作用域。Headless 不激活设备与视口，仍验证被选择插件的身份和版本；需要被排除图形能力的更新插件报缺能力。Graphical 不能因后端缺失降为 Headless。启用配置与内容身份保留全部原始选择。

## 时间、输入与线程

宿主向唯一 `IApplicationDriver.AdvanceAsync` 提交已过去的受控时间，不要求阻塞 while。可变更新每次正 delta 执行一 tick；固定更新累积时间，每次最多 MaxCatchUpSteps，保留不足一步余量，超过追赶上限的完整步计入 DroppedTime。MaxFrameDelta 限制单帧时间，默认 250 ms；被限流的时间也计入 DroppedTime。不用墙钟参与权威模拟。无界面可连续注入逻辑时间，无实际帧率等待。

Paused 跳过模拟并清除积压时间，不冻结展示。StepAsync 仅在暂停时执行一个固定步（可变模式为 1/60 秒），不重置输入序号。输入按递增 Sequence 入队并复制字节；旧序号拒绝。一个 tick 的输入是只读快照，只在第一个追赶 tick 消费一次；所有更新贡献观察同一批次，插件按自己的输入契约处理。暂停时输入保留，停止时丢弃。每实例限 4096 条积压输入、每条 1 MiB，超预算显式拒绝。

update 在线程池中串行执行，不捕获 UI 同步上下文。并发或递归推进直接拒绝，慢操作未结束时不会再次启动。每个 update 异步准备结果，返回可选同步提交动作；整 tick 的准备全部成功且未停止／取消后，在串行安全边界执行提交。插件须将准备阶段的可见修改留到提交，提交须由插件保证原子且不抛异常；小核心无法回滚任意插件的内存副作用。任何故障停止该实例推进并标明插件与贡献。正常失败和取消保留上个成功 tick 的展示状态；提交违反不抛异常契约时禁止继续 render，以免展示部分提交。

render 是同步回调，在宿主调用线程执行，图形宿主负责在设备线程调用；它消费已发布只读状态，不接收提交入口。模拟发布与绘制互斥，准备阶段可以与绘制并行。每个视口有独立展示时间，暂停模拟时仍绘制；多视口只读同一 CompletedTick，不能触发 update。绘制故障只停止该视口并定位插件，不改变已提交模拟。投影权限由所选桌游能力保证，小核心不向 render 自动发放权威服务。

## 停止与故障

StopAsync 立即拒绝后续输入及推进，取消未完成任务，并等待任务退出再释放资源。迟到准备结果不提交。不强制终止不合作的 .NET 回调，插件必须响应取消；隔离执行器的超时和 worker 终止属于提供者责任。停止与释放可重复调用，失败清理尝试所有资源并汇总错误。启动失败清理已完成及失败插件的 effect，不留可执行回调。

`RuntimeHost.StartScopedAsync` 接受显式父宿主、作用域、模式与时钟；实例的 `Driver` 是唯一推进所有者，视口的 `Render` 只绘制。可选 `dispatch` 同步封送该作用域的激活与 `Own` 资源释放，图形适配用它维持设备线程归属；默认在当前工作线程执行。render 必须由宿主在正确线程调用。异步启动与关闭应被等待，UI 不得同步阻塞等待自身 dispatch。父宿主关闭会等待仍在初始化的子作用域，不提前释放其依赖。积压输入另有 4 MiB 总字节上限。

契约验证：`dotnet run --project tests/Application.Tests`。宿主实现及桌游迁移分别验收；该契约文档不能作为真实图形或非桌游游戏交付证据。

## 内容与兼容门面

`BoardGame.AppRuntime.GameRuntime.Load(source, catalog, preset)` 和 `GamePackage.Pack(directory, destination, catalog)` 位于 Application.Core，获准 `ContentCatalog` 由宿主传入。加载只激活提供者声明的准备能力；`IContentLoader` 返回自身 format/version 的初始内容载荷，通过 `IContentSource` 读取受路径和大小限制的内容。所有预设及其实际读取的依赖都纳入身份，运行状态由 update 插件自选内存模型，不要求每帧 JSON。

通用快照为 `kind: application-content, format: 1`，通用包为 `.bgm format: 3`。快照同时保留完整内容字节、有效启动身份和提供者载荷；恢复只从快照读取，重新准备并比较载荷、版本、内容指纹与完整文件集合。修改声明以外的载荷、缺件、重复路径、未知版本或内容不一致拒绝。目录和包使用同一准备路径，不执行规则或更新回调。

桌游基础提供者位于 `BoardGame.Core`，只依赖 Kernel 与 Application.Core；实体／席位／命令等仍属于 Kernel.Abstractions 的桌游契约，小核心不引用它。权威、流程事务、记录与会话服务要求安装目录授予 `board.authority` 权限；权限不来自启动配置。普通规则、CPU 或展示即使声明依赖也不能取得受保护服务。投影服务使用独立包装，可信适配按已授权席位生成 `ISeatProjection` 后只将它交给展示，包装不实现权威调试接口。`IAuthoritySession` 明确属于可信系统／作者接口，由原 Runtime 程序集转发到 BoardGame.Core。

原 `BoardGame.Runtime.GameRuntime.Load`／`GamePackage.Pack` 保留既有 LoadedModule、桌游快照与 format 2 包的显式适配，不自动升级旧数据；新增传入 `ContentCatalog` 的重载转交上述通用实现。仅依赖小核心的宿主直接引用 `BoardGame.AppRuntime` 门面，既有桌游宿主保留原命名空间。通用加载器不尝试旧桌游解析器；旧桌游调用也不静默解析 format 3。插件作者应显式选择对应数据契约，后续默认桌游组合迁移不能靠伪造桌游数据兼容非桌游内容。

## 执行器与桌游绑定

`Lua.Execution` 仅依赖通用抽象和 MoonSharp，提供 `LuaExecution`（单次 VM）与 `LuaProcessSession`（受限工作进程）。调用者显式传入函数、JSON、种子和 `LuaCallShape`；执行器不识别 actions、commands、winners 或规则验证入口。数组结果、数组字段和需要验证的导出函数由绑定指定。每请求重建 VM，OS 进程按作用域复用；保持指令、内存、墙钟、输入输出预算与父进程退出清理。

现有 `LuaWorker`／`LuaWorkerSession`／`LuaEvaluator`／`LuaRules` 名称和桌游语义保留在 Lua 绑定／worker 程序集，规则、CPU 与流程调用继续使用受限输入及原重载边界。worker IPC 升为 api 2，携带显式结果形状；缺版本／未知版本拒绝，发行时必须使用匹配 worker，不回退旧协议。通用宿主可直接引用 Lua.Execution 并传入获准 worker 路径，无需引用桌游会话。共享随机算法移入通用抽象并保留旧类型转发，权威随机状态的所有权仍在桌游事务。

`Wasm.Execution` 只引用固定版本 Wasmtime，管理编译模块、每次调用的新 Store、fuel／内存／模块数量与体积预算及释放。调用期间的 `WasmCall` 不能跨调用保留使用，不开放 imports、WASI 或宿主状态句柄。桌游 `WasmPlugins` 继续负责 API／版本／哈希／权限校验及 component／command／snap／container 绑定，结果转换为标准命令进入原权威事务。没有启用 WASM 插件的组合不创建执行器。

执行器使用显式 trap 检查，兼容 Windows CET 保护；不改变系统安全策略。48.0.2 原生库支持此配置，但托管绑定尚未暴露，因此 `WasmtimeTrapCompatibility` 只获取配置 SafeHandle 调用对应原生 setter，失败时拒绝启动。升级至包含[上游 PR 390](https://github.com/bytecodealliance/wasmtime-dotnet/pull/390) 的正式版本后应删除此兼容层并使用公开 API。这是普通 .NET 的已验证路径，不能推导为 NativeAOT 验收通过。

## 流程推进

`FlowRuntimePlugin.Create` 可独立登记规则与 `IFlowProgression`，只依赖桌游权威及事务能力，不依赖 session 或 driver。推进契约位于 BoardGame.Core；会话仅通过该接口协调 Advance／Submit／Resume，Cancel 可并发发出。自动节点的效果及续行仍在同一权威事务内提交，规则控制来源互斥及不支持 WASM 组合的诊断保持原样。

读取投影、render、启动观察视口和空的 update 不推进流程。协调者显式 Advance 后停在玩家等待边界；重复 Advance 不再重复查询等待节点的动作脚本。节点刚进入等待、玩家提交后仍即时验证下一组动作，保留原故障语义。取消、预算暂停及已提交恢复边界须显式 Resume；图形生命周期不能隐式恢复或重开流程。

## 桌游记录与观察回放

`BoardRecordingPlugin.Create` 注册桌游记录能力，只依赖受保护的权威接口。权威内核在事务中生成命令、随机结果和流程续行日志，记录提供者捕获该日志并在脚本切换的同一串行边界记录版本，不依赖事件订阅顺序。通用 Application.Core 不识别这些格式。

可信宿主可调用 `BoardRecordingPlugin.Observe(recording, authorizedSeat)` 得到与实时展示相同的 `ISeatProjection`。它验证并应用已经记录的结果，不启动规则／CPU／WASM／流程推进器；返回对象只保留该席位的投影，每次读取均复制，没有权威或提交接口。席位授权由宿主负责，权威记录包含秘密，不得发送给玩家；联网玩家记录仍通过自己的历史投影读取器处理，不能转换为权威记录。保留原 format 1／2 及显式验证路径，未知格式和损坏数据拒绝。

## 默认桌游 update

`session.local/advance` 是 Instance 的 update 贡献。它消费 `board-preparation`、规则、记录及已选择的 CPU／流程／效果；内核提供受权限保护的 `IBoardTransactionSource`。候选分支复制可变状态，复用只读的私有初始值和历史条目，日志列表独立。发布检查原权威版本，成功后关闭候选句柄，不能通过保留句柄绕过下一次提交边界。

`board.command`、`board.controller`、`board.resume` 输入使用 `BoardUpdateRequest`，必须包含权威日志 Revision；提交需要 Seat／Command，控制方式变更需要 Seat／Controller。原始输入 Sequence 去重，候选内每条操作再次检查 Revision，结果通过 `IBoardUpdateState` 返回，最多保留最近一 tick 的 256 条。无桌游输入的 tick 最多执行一次 CPU 决策；等待真人不创建候选分支。输入 tick 不附带额外 CPU 决策，绘制不推进游戏。桌游逻辑不使用显示帧率或墙钟作为随机来源。

准备在驱动线程池上执行，规则／效果／流程事务只修改候选内核；流程候选使用独立执行上下文，准备结束即释放。所有阶段成功后才在发布边界切换权威状态、日志、诊断及流程状态，取消或其他阶段失败不发布。发布一旦开始，取消从下一边界生效，不能把多个提交动作拆成半帧。准备期间旧投影可读，直接变更接口明确拒绝并发；准备完成后发生的直接变更使候选过期。

脚本重载仍在串行边界校验候选并记录版本，失败保留旧版本；与正在准备的 update 并发时明确拒绝。玩法回调必须保持原有纯决策约定，不能在提供者私有字段中保存未参与事务的游戏状态。成功终局／故障后的执行器仍由插件作用域持有，关闭作用域统一释放；这不授权后续 tick 继续决策。

## 宿主驱动

`PlaySession.SubmitHumanAsync`／`StepCpuAsync` 使用每局独立串行门禁和同一 `IApplicationDriver`，原同步方法只是该路径的兼容包装。输入由驱动分配序号；取消或未能启动的请求撤回仍在队列里的输入。自动步骤使用受控的 1/60 秒逻辑单位；宿主也可用 `AdvanceAsync(elapsed)` 注入经过的时间。暂停时显式单步执行一个逻辑单位，普通 Advance 不推进。

PlayerSession 和 LocalTableSession 直接等待异步入口；CLI 批量、独立命令行、内嵌／外部调试的同步兼容调用最终都进入同一驱动。规则与 CPU 的 Lua 请求接收当前 update 的取消令牌，按调用上下文隔离，取消不污染下一次执行。DisposeAsync 取消正在执行的请求，等待驱动停止后释放插件；同步 Dispose 用于同步所有者。

Editor 的 sessions 插件在工作线程创建内嵌会话，捕获当前模块与文档代次；停止或切换文档取消尚未完成的启动，外部 Player 握手同样可取消并回收进程。调试暂停／单步绑定驱动时钟，观察和面板重建不触发推进。图形线程仍负责现有视口设备调用，WindowsDX render 接入在后续单独迁移。
