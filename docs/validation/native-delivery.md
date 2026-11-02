# 原生发行联合验收进度

## 2026-11-02 GPU 启用后的最终干净环境验收

用户启用 Sandbox GPU 后，在新会话 `b9aef2da-c531-4301-b2b5-026434ed1929` 重新复制最终产物并验证。主机 `AllowVGPU=1`，客机枚举到 Intel Graphics；Win32 探针对 PATH、标准系统／用户 .NET 目录和安装登记的检查均为 false。沙盒只接收发行包、测试探针、游戏数据及证据目录，没有引擎源码、SDK 或 Editor 安装。测试期间没有安装系统 .NET。

SDK04 最终四种桌游组合及 Motion 的环境阻塞已解除，证据见 [GPU 沙盒最终验收](evidence/native-delivery/sandbox-gpu-2026-11-02/)。以下分别记录自动探针与正式发行程序，不将二者混为同一测量：

- `board-regression-11` 对无脚本、WASM、外部 CPU、WASM＋外部 CPU 各完成 5 局，共 20 局；包含真实第三帧、隐私投影、暂停后绘制不推进更新、固定种子重开、完整对局、记录回放、保存中关闭及原生窗口释放检查。
- 同份代码生成普通自包含探针，在同一沙盒再完成 20 局；四组合记录哈希与原生探针逐一相同，见 `comparison.json`。这是相同输入与宿主逻辑的普通／原生对照，不声称泛用 Player 与专用入口的全部文件身份相同。
- 四个 SDK04 正式 `Play.exe` 均实际显示牌桌、响应鼠标并正常退出。最小版完整完成洗牌、抽牌、结束、保存与回放；组合版完整完成洗牌、抽牌、WASM 加二、外部 CPU 结束、保存与回放，并实际重新开局。独立 WASM 版验证加二，独立 CPU 版验证交给 CPU 后结束；完整五局回归由对应探针覆盖。
- Editor 最终草稿原生导出实际显示中文计数和图片，WASM 输入更新为 2；同一草稿普通独立包也显示相同内容，完成 WASM、CPU、保存与回放。此前 `editor-author-final04.json` 保留导入、未保存草稿、三导出、Undo/Redo、保存重开与源文件保护证据。
- `final-motion-05` 在干净沙盒实际显示，方向键使圆点移动，空格使其复位，正常关闭。独立／多嵌入视口、持续输入到目标、暂停绘制、单步、焦点取消与关闭中加载的完整自动回归仍引用 `motion-lifecycle-fixed.json`，不把本次短按键操作描述为持续按键测试。
- 七个正式窗口（四组合、Motion、草稿原生、草稿普通）均正常关闭，CLI 返回 0；最后查询 Play、Player 与 Native.Board.Startup 进程数量为 0。正式记录和截图已保存，客机 EXE 哈希见 `guest-binary-hashes.json`。

新进程第三帧测量：四组合原生分别为 4759／4375／3815／3878 ms，普通探针为 5903／5571／5552／5219 ms；对应工作集与私有内存见 `measurements.json`。这是虚拟机各组合单个新进程样本，含图形初始化，系统缓存未清空且主机曾并行构建普通探针，不用于宣称可重复的性能收益。包体积、严格构建日志、闭包／裁剪和未选依赖排除继续引用 SDK04 报告。

本轮联合发行验收通过；Lua 原生路径仍明确不支持，不能由 WASM 组合通过推导其可用。全范围整体审核是后续独立步骤，尚未完成。以下失败、暂停及中间版本记录作为历史证据保留，不覆盖本节最终结果。

## 2026-11-02 Sandbox 恢复连接后的复验（13:00）

用户恢复 Sandbox 连接后继续验收。当前会话可操作，官方 `wsb` CLI 已将最终测试包复制至沙盒本地 `C:\NativeValidation`（515 个文件，复制失败数为 0）。独立 Win32 环境探针未发现 PATH、标准系统／用户目录及 x64 安装登记中的现代 .NET；这证明本次检查范围内无系统 .NET，不代表已完成图形验收。

最终 `board-regression-11` 探针对最小无脚本组合执行 `--play`，在图形初始化阶段报告缺少 `ARB_framebuffer_object`／`EXT_framebuffer_object`，退出码为 1，未得到首帧或完整对局。SDK04 最终 `release-minimal-05` 的 `Play.exe` 也实际打开并显示同一诊断，随后正常关闭（退出码 0）；不能把错误窗口或零退出码记作游戏运行通过。测试包中的正式 EXE 与最终产物哈希一致。

主机只读查询发现 `HKLM\SOFTWARE\Policies\Microsoft\Windows\Sandbox\AllowVGPU = 0`；沙盒枚举到 `Microsoft Remote Display Adapter`。微软说明禁用 vGPU 后使用 WARP 软件绘制（[配置说明](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file)）。用户确认是为解决 Sandbox 连接问题而禁用 vGPU，因此保留该设置，不为继续测试重新启用。此次桌面连接成功不等于满足 MonoGame DesktopGL 的 OpenGL 要求；主机策略、显卡驱动和渲染后端均未修改，图形验收等待合适环境。

沙盒默认拒绝执行 PowerShell 脚本，未更改执行策略；环境探针、复制及游戏分别经 CLI 直接执行，仅以只读命令查询显示适配器。原始报告、阶段 CSV、二进制哈希、策略值和正式游戏诊断截图见 [本次 Sandbox 证据](evidence/native-delivery/sandbox-2026-11-02/)。其余图形组合没有重复执行已知失败的设备初始化，最后两项与整体审核继续保持未完成。

## 2026-11-02 续接结果

用户已要求继续完成。此前 SDK02 的四个桌游组合和 Motion 均严格原生编译通过；对应预编译 Player 与 Editor 草稿三导出、撤销重做、保存重开回归通过。真实构建服务与作者提供者的编译失败、活动取消、重试、源输入保护和输出原子性回归通过。项目依赖边界检查通过。

追加关闭回归发现两个生命周期缺口：保存记录未纳入窗口的活动任务，以及 Avalonia 延后销毁原生子窗口时主消息循环可能先退出。现在记录保存与重开共用串行任务，关闭取消并等待保存，临时文件完成后才移动为最终 JSON；`PlatformViewport.Shutdown` 在 Dispose 后包含真实子窗口销毁，所有者先从可视树移除再等待。修复前 `close-save-minimal.txt` 报告原生视口残留；修复后 `jit-close-fixed-*-10.txt` 与 `native-close-fixed-*-11.txt` 分别对四组合各跑五次完整游戏、重开和保存中关闭，共 40 局通过，普通与原生记录哈希逐一相同。GL 双视口、重建、输入、初始化取消及设备失败回归见 `gl-lifecycle-fixed.json`，设备／后端／原生窗口最终均为 0。

修复后的静态 SDK 为 `sdk-complete-04`，原生游戏目录为 `release-*-05`；此前 SDK02/03 产物不能替代此次关闭修复的验收。作者 CLI 实跑发现说明中的 profile 缺少 `author.validation`，已补齐并通过样例导入、操作自描述及 `project.validate`，证据为 `cli-native-describe.json` 和 `cli-validate.json`。

SDK04 的四种桌游产物均以零诊断完成严格原生构建，完整目录大小依次为 47,548,760／69,826,103／47,553,569／69,830,928 字节；准确文件和引用集合见 `sdk04-*` 子目录。更新后的 Player05、Editor 与 SDK04 再次完成草稿试玩、停止与插件停用、三导出、Undo/Redo、保存重开及源文件保护，报告 `editor-author-final04.json` 全部五项通过。

桌面解锁且前一组构建结束后，按 JIT／AOT 交替顺序各启动三个新进程，以真实第三帧作为首帧测量终点。相同 WASM＋CPU 输入、相同探针代码下，JIT 为 1,947／1,898／1,866 ms，AOT 为 837／849／825 ms；首帧工作集分别约 332～333 MB 和 276～278 MB（十进制）。原始 CSV 与 `unlocked-measurements.json` 保留。它们是本机探针的新进程样本，系统文件缓存未清空，不代表正式泛用 Player、磁盘冷缓存或虚拟机性能。

Windows Sandbox 重新创建后仍无法建立可用桌面连接。用户改用已连接的 Hyper-V／mstsc 虚拟机；可查看该桌面，但当前主机账户没有 Hyper-V 管理权限，RDP `\\tsclient\C` 访问失败，文件剪贴板复制未观察到成功。此环境的最终产物验收尚未通过，不沿用此前沙盒的环境清单冒充新虚拟机证据。

SDK04 的非桌游 Motion 最终产物为 `final-motion-05`，严格构建零诊断，引用清单不含 BoardGame／Kernel／Lua／Wasmtime；本机实际窗口显示与关闭通过。`motion-lifecycle-fixed.json` 验证独立及双嵌入视口、连续方向键与目标、暂停绘制、单步、共享实例、焦点取消、反复启停及加载中关闭，最终设备／后端／原生窗口数为零。该报告是本机图形回归，不是新虚拟机验收。

已准备 `artifacts/HyperVValidation/NativeValidation`：四种 SDK04 原生游戏、Motion、Editor 的普通／原生导出、原生回归探针及四个测试快捷方式。整个目录复制到虚拟机 `C:\NativeValidation` 后可运行；快捷方式固定指向此位置，报告写在目录根部。RDP 文件管理器的“粘贴”菜单为灰色，等待用户提供共享目录或启用驱动器重定向。不要为此改变 Hyper-V 权限、安全设置或虚拟机已有应用。

### Hyper-V 续接结果（10:39～10:51）

用户启用 RDP 驱动器重定向后，完整测试包已从 `\\tsclient\C` 复制到虚拟机本地 `C:\NativeValidation`。运行本地 `test-minimal.lnk` 后，真实 AOT 探针进入图形初始化并报告：`MonoGame requires either ARB_framebuffer_object or EXT_framebuffer_object`。未得到首帧和完整游戏结果，不计作通过；报告没有触发探针的原生窗口残留检查。

独立 Win32 环境探针检查 PATH、标准系统／用户 .NET 目录和安装登记：`dotnetOnPath`、`programFilesDotnet`、`dotnetInstalledRegistry` 均为 true。因此这台现有虚拟机也不能证明无系统 .NET 发行；没有卸载其现有软件、安装驱动或更换后端。两项阻塞分别是图形能力不足与干净环境条件不符，不能用一次原生进程成功启动替代验收。原始报告、环境探针源码和可见诊断保存在 [Hyper-V 证据](evidence/native-delivery/hyperv-2026-11-02/)。待用户提供具备所需 OpenGL 能力且无系统 .NET／SDK／作者工具的环境后继续最终产物验证。

本机普通与原生探针各 5 次冷启动，每个进程完成 5 次启动／重开和等待关闭，共 50 次首帧检查；另一个真实原生探针对无脚本、WASM、外部 CPU、WASM＋CPU 四种组合分别完成 5 局洗牌／抽牌／结束、脚本效果、CPU、秘密投影、暂停绘制、固定种子记录与回放校验。证据见 [最终回归目录](evidence/native-delivery/final-2026-11-02/)。

| 最终原生产物 | 完整目录字节 | EXE 字节 |
| --- | ---: | ---: |
| 最小无脚本桌游 | 47,544,664 | 27,003,904 |
| WASM | 69,820,471 | 27,184,640 |
| 外部 CPU | 47,549,472 | 27,007,488 |
| WASM＋外部 CPU | 69,825,808 | 27,188,736 |
| 非桌游 Motion | 44,421,907 | 23,888,896 |

普通／原生首帧与内存原始 CSV 已保留；采样期间存在并行构建及锁屏，不能用这些数值声称可重复的性能收益。沙盒自动图形探针在 120 秒内持续完成三轮绘制后超时，阶段有进展；用户已解锁桌面，正在补可见图形会话的干净环境验收。这些超时没有计作通过。

同步 UI 压力用例仍可重现等待：原生反汇编确认主线程停在 `GameRuntime.Prepare` 等待 `RuntimeHost.StartScopedAsync`，追踪同时显示后台初始化已经结束。正式入口把内容准备放在后台，前述异步回归通过；不将此证据扩大为已修复 .NET 或 CordiSharp 的底层问题。临时初始化追踪已从生产源码移除。此前 45 秒总预算可在第五次绘制时耗尽，现用阶段 CSV 和 120 秒总预算区分无进展与慢运行。

下面保留上一轮记录；最终可见窗口验证和完整整体审核尚未关闭。

日期：2026-10-31。**尚未完成最终验收。** 用户要求本轮在追加的 20 分钟窗口内结束，未通过项保留未勾选；本记录保存已复核证据及续接入口，不替代完整整体审核。

## 已有证据

- `minimal`、`wasm`、`external`、`wasm-external` 四个真实 Windows x64 原生游戏均通过严格 ILLink／ILC 编译，无警告豁免。发行清单和引用集合保存在 [evidence/native-delivery](evidence/native-delivery/)。
- 干净 Windows 10.0.26100，无源码、SDK、系统 .NET 和作者工具：四个组合均已实际打开、操作和关闭。组合版真人输入洗牌、抽牌、WASM 加二及交给外部 CPU 后结束；保存记录通过回放校验。最小版也完成洗牌、抽牌、结束和记录校验。
- 普通独立 Player 与原生组合版实际记录回放后的权威状态相同，SHA256 为 `DA64C7EF6B88D29D1CBFF9103B65873C6F4E25E4918795995BEAD20E80778969`。原生发布固定内置实现指纹，因此内容／记录身份与未固定内置指纹的普通版不同，不能直接声称两份记录字节相同。
- 普通 UI 验证四个组合各两次重开，验证隐私投影、暂停后 render 不推进 update、规则输入、CPU、WASM、记录回放、关闭后原生子窗口数为零。组合版额外 30 次重开记录哈希均相同，报告见 `ordinary-30-restarts.json`。
- Editor 端实际导入外部 CPU，修改未保存的中文状态，草稿试玩、停止、重开及停用试玩插件等待退出；同一草稿分别 NativeAOT／普通 Player／数据包导出，Undo/Redo、保存重开和原磁盘文件未修改均通过。报告 `editor-author.json`；原生草稿的中文、图片和 WASM 输入也在干净 Windows 观察。配置 UI、后端切换和三提供者独立启停的既有证据分别见 [配置](editor-runtime-configuration.md)、[插件管理](editor-runtime-management.md)、[后端](editor-backends.md)、[原生导出](editor-native-export.md)。
- 新 SDK 的非桌游 Motion 原生编译通过，引用清单没有 BoardGame、Kernel、Lua 或 Wasmtime；其新产物的干净环境复测仍需完成。既有通用入口证据见 [专用原生入口](native-game-entry.md)。
- 缺声明、假声明导致的真实 IL2026、传递依赖、错误架构、缺件、篡改和取消的已有验证见 [构建服务](native-build-service.md) 与 [入口完整性](native-game-entry.md)；最终版本须按改动范围补回归。

## 体积与测量限制

| 原生产物 | 总字节 | EXE 字节 |
| --- | ---: | ---: |
| 最小无脚本桌游 | 47,542,615 | 27,001,856 |
| WASM | 69,818,935 | 27,183,104 |
| 外部 CPU | 47,547,425 | 27,005,440 |
| WASM＋外部 CPU | 69,823,759 | 27,186,688 |
| 非桌游 Motion | 44,421,395 | 23,888,384 |

这些数字对应 `sdk-complete-01` 构建，后续修复后必须重新确认。WASM 专属 `wasmtime.dll` 不进入无 WASM 产物；通用 Motion 不引用桌游契约。审计文件记录完整哈希；这些证据证明选择闭包，不以试玩覆盖率裁剪。

普通完整 Player 组合目录为 202,103,036 字节，包含泛用预编译宿主能力，与专用原生闭包范围不同。单次普通测量 `WaitForInputIdle` 为 1,597 ms，随后 3 秒工作集 172,781,568、私有内存 93,585,408 字节。原生同一测量得到 283 ms、92,409,856 和 62,087,168 字节，**但该次窗口之后发现停在初始化阶段，故原生数值无效，不可用于宣称启动或内存收益**。保留原始测量文件，后续改用“真实首帧完成”的条件并进行重复采样。

## 当前阻塞与诊断

一次原生组合版冷启动停在空白窗口，主线程在 `GameRuntime.Build/Prepare` 同步等待 `RuntimeHost.StartScopedAsync`，WASM／SDL 尚未加载。符号化线程栈见 `native-hang-symbols.txt`；同组合另一个 Editor 导出实例正常，但成功一次不能覆盖故障。

已修复一个可稳定重现的问题：`ApplicationCatalog.StartAsync` 在返回 Task 前同步重建内容，可能阻塞 UI。`Application.Tests preparation-isolation` 修复前阻塞约 1.014 秒而失败，修复后通过；现在内容准备和激活都在后台执行。**这只证明调用线程不再同步阻塞，尚不能据此宣称原生启动故障已经消除。**

`tests/Native.Board.Startup` 是独立严格 AOT 回归探针：真实窗口连续五次启动／重开、绘制和等待关闭。最初 12 秒全程 watchdog 的前后版本均超时，尚不能区分慢启动与挂起；已改为 45 秒并标明阶段。这个探针不进入正式游戏发行。

追加结果：45 秒阶段探针已完整通过五次启动／重开、真实绘制及等待退出（`startup-diagnostic.txt`）；最新 `sdk-complete-02` 的组合版 `board-final-02` 严格构建通过，并在干净 Windows 正常显示首帧。仍需更多冷启动和最终四组合回归，不能仅凭这一轮成功关闭原先的偶发故障。最新 Editor 构建零警告／零错误；小核心全部测试、入口种子／控制器、桌游记录恢复续局、发布计划及完整性测试和项目依赖边界检查均通过。

再次独立启动探针也通过五次重开（`startup-repeat-1.txt`）。最新正式组合的清单、编译日志及实际首帧保存在 `final-combined/`。本轮暂停时已正常关闭正式游戏窗口，保留沙盒和构建目录便于续接。

## 续接与验收门槛

1. 收敛原生启动／重开超时，记录明确根因；用真实 AOT 探针及最终 `Play.exe` 验证首次启动、重开、退出。
2. 用最终 SDK 重建四个组合，补最终 Editor／CLI 产物与无桌游 Motion 的干净环境回归；已有中间版成功不冒充最终版。
3. 补真实首帧条件下的重复启动／内存测量、最终文件与引用清单，以及最后变更的失败／取消回归。
4. 校对 [使用说明](../native-publication.md)、作者协议和 ADR；通过联合验收后再勾选 ROADMAP 两个剩余任务并分别提交。
5. 全范围完整审核、必要修复和最终回归仍未执行，不用本轮快速检查代替。

本地构建／完整原始证据仍在忽略目录 `artifacts/native-aot/`，原生符号在 `%TEMP%/bgb-native-build-logs/`；仓库仅保存必要报告、记录和截图。
