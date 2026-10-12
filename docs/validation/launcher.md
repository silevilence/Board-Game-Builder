# 多版本启动器实施与验收记录

对应 ROADMAP「基于 GitHub Release 的多版本启动器」。未完成的阶段不视为验收通过。

## 完整版本依赖清单（2026-10-12）

实现 `toolkit.json` API 2、实际产物清单生成／验证及按哈希对象生成不可变组件归档，接入完整工具包构建。格式及职责边界见 [清单说明](../toolkit-manifest.md)。API 1 的 V0.1.0 保留明确旧格式识别，不推断共享布局。

- `python -m unittest discover -s scripts/tests -v`：20 项通过（原发布流程 13 项，清单 7 项）。覆盖旧版／未知格式、缺项／多项、Windows 路径冲突、非法依赖、损坏文件／组件、重复 JSON 键、同名不同内容、重复构建及仅产品变化时稳定组件复用。
- 使用既有 V0.1.0 实际完整发行目录的副本生成新清单，原验收目录未修改。副本为 `artifacts/launcher-manifest-toolkit`，组件为 `artifacts/launcher-components`。完整清单、文件与组件逐项验证成功；原目录的旧清单识别成功。
- 实测清单记录 2,113 个文件、8 个宿主、54 个库身份、38 个组件；原文件逻辑总量 1,193,157,312 字节，按 SHA-256 的唯一内容量 289,803,319 字节，组件 ZIP 总量 100,017,171 字节。这是已有包的清单与组件测量，**不是后续新发行布局或物理存储去重的验收**。
- 真实 runtimeconfig 声明 .NET 10.0.12，图形宿主同时需要 Microsoft.NETCore.App 与 Microsoft.WindowsDesktop.App。CLI 与 Lua worker 使用核心框架。运行插件目录从正式 Player 清单读取，第三方版本来自 deps。

组件来源目前是不可变附件的声明，尚未发布。没有修改 V0.1.0 附件或 tag，没有将此次本地生成标记为实际 Release 验收。当时后续阶段尚未完成；后续结果分节记录。

## 发行包内去重与私有运行时定位（2026-10-12）

交付 C# Native AOT 公开入口和 `hardlinks-v1` 布局。完整 ZIP 只保存每个 SHA-256 的唯一规范文件，入口校验后建立同卷硬链接，再启动 `.payload` 内的宿主；各宿主及 Lua worker 的原依赖路径引用同一份物理数据。普通发行物移除第三方 PDB，导出仍使用原正式 Player 预检及独立复制。

- 28 项脚本／原生入口测试通过，覆盖并发首次启动、输入／输出／退出码／工作目录、内容损坏、非共享复制品、越界路径及格式错误。Release 全解决方案构建 0 警告、0 错误，依赖边界检查通过。
- 完整执行 `publish-toolkit.ps1 -Tag V0.1.0 -Output artifacts/compact-build/toolkit -Archives artifacts/compact-build/release`，从源码完成所有宿主和原生入口构建、完整清单、组件、布局压缩、CLI 校验、独立导出及三局 Player 回归，最后生成 ZIP。日志为 `artifacts/compact-build.log`。
- 该文件是本地开发候选：沿用当前 0.1.0 产品声明，基于 `5ba0887` 加本轮工作区改动，**不是已发布 V0.1.0 的替换物或新的实际 Release**。候选 SHA-256 为 `a5ecc7c572059fb7ca3e73a12a6183d20271a38e49aeeabffde881901f7ccba5`；原正式附件未修改。
- 在新 Windows Sandbox `10.0.26100.0` 中确认无 dotnet、无 x64／x86 Runtime，断网且不映射源码。中文／空格路径解压后，两次真实 Editor 验证（移动前、同卷移动后）各 11 项通过，覆盖图片导入和 Undo、保存重开、内嵌／外部对局、依赖定位及正式导出。
- 同一干净环境完成两种作者格式的 CLI 新建、验证、seed 17–19 CPU 重跑／回放及打包，Copy／Junction SKILL 安装维护，CLI 正式导出、独立 Player seed 42–44 批处理及图形对局。`artifacts/compact-build-sandbox/acceptance/result.json` 为 `success: true`。

实测对比（字节；物理分配不包含清单本身、目录元数据和可写 `.state`）：

| 指标 | 原 V0.1.0 发行目录 | 本轮完整源码构建 |
| --- | ---: | ---: |
| ZIP | 462,143,000 | 77,433,023 |
| 逻辑文件字节 | 1,193,157,312 | 992,337,927 |
| 物理产品内容字节 | 1,193,157,312 | 193,218,601 |
| 本机实际文件分配 | 1,196,994,936 | 193,970,672 |
| 逻辑文件／物理文件数 | 2,113／2,113 | 2,128／407 |
| 核心运行时物理文件身份 | 8 | 1 |
| 桌面运行时物理文件身份 | 3 | 1 |

`measure-toolkit.ps1` 以 coreclr.dll 和 System.Windows.Forms.dll 的文件 ID 统计核心／桌面运行时份数，并对**每个**内容哈希检查只有一个物理文件。基线报告为 `artifacts/compact-build/baseline-storage.json`，新包报告为 `artifacts/compact-build/storage.json`。沙盒独立测量同样为 407 份物理内容，分配 193,967,064 字节；分配差异来自文件系统。ZIP 的 408 个条目（含清单）逐项哈希无重复。

验证中保留了两个问题记录：Windows PowerShell 5.1 脚本需以 UTF-8 BOM 传入沙盒，且不能使用 PowerShell 7 对哈希表的 Measure-Object 属性适配，测量脚本已兼容；早期原型在与源码构建并行时一次 CPU 验证出现 Lua 限时错误，同种子通过包装／直接入口四次对照均成功。未稳定复现或归因该超时，也未修改 Lua 限额；随后从完整源码产物串行执行的全新沙盒验收全部通过。早期记录保留在 `artifacts/compact-sandbox-final/`。

精简包、组件实际发布、独立启动器及最终双版本验收仍未完成，不勾选后续任务。

## 独立包、精简包与完整游戏导出（2026-10-12）

同一份自包含发布产物派生两个模式，再生成各自的原生入口布局、完整清单、组件和 ZIP。精简包移除私有运行时，入口检查系统框架、x64 架构、最低补丁和桌面框架依赖；正式导出依照独立的锁定描述恢复可再分发 Player，缓存复用后仍以独立复制方式输出游戏。

本地候选位于 `artifacts/dual-verified/`，基于 `78855f8` 加本轮工作区实现，沿用 0.1.0 产品声明，**不是新 Release，也不替换正式 V0.1.0**。

| 指标 | 独立包 | 精简包 |
| --- | ---: | ---: |
| ZIP 字节 | 77,852,138 | 31,115,941 |
| 逻辑文件／唯一物理文件 | 2,128／407 | 552／193 |
| 物理产品内容字节 | 194,252,907 | 81,407,958 |
| 实际文件分配字节 | 195,006,960 | 81,768,944 |
| 私有核心／桌面运行时份数 | 1／1 | 0／0 |

独立 ZIP SHA-256：`46c4a9f3cf83f6f5e0634f804082d05f34796892a37d704e84afdc6b65cf0f22`；精简 ZIP SHA-256：`f9ba03cd3c393a0af4f7e7c842179e4651c12e06fe4c3d3e0a50a7ef4cea2177`。两个包均通过文件清单及完整导出组件校验；两种模式共引用 40 个固定组件，当前仅本地生成，尚未实际发布。

- 脚本与原生入口相关测试 31 项通过；C# `export-dependencies`、`system-runtime`、`author-operations`、`author-exports` 通过，依赖边界检查通过。覆盖离线缺失不生成半成品、HTTP 获取与本地组件、完整性失败、归档越界、并发缓存、取消、长路径、架构／版本及兼容补丁选择。
- `artifacts/dual-verified.log` 记录完整构建和两种模式的 CLI 校验、导出及 Player 回归。精简 ZIP 首次归档时输出目录已被测试沙盒映射，重命名遇到持续 Win32 共享锁；短重试仍拒绝发布。停止该沙盒后重新归档成功，记录在 `artifacts/dual-verified-archive-retry.log`。验收只使用最终 ZIP 与其校验信息。
- `artifacts/dual-verified-standalone-sandbox/acceptance/result.json` 为成功：无 SDK／Runtime、断网、不映射源码，中文及空格目录移动前后各 11 项真实 Editor 检查；两种作者格式的 CLI 创作、CPU 重跑／回放、打包、SKILL Copy／Junction 维护、正式导出及图形／批处理运行全部通过。
- `artifacts/dual-verified-slim-acceptance/runtime-checks.json` 为成功：缺少核心、错误 x86 架构、10.0.11 旧补丁、缺少桌面框架均返回 1 且提供具体诊断；兼容核心框架返回 0。随后仅安装 .NET Core／Desktop Runtime 10.0.12；`installed-sdks.json` 为空数组。官方 ZIP 已依照 [微软 10.0 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) 的 SHA-512 校验，未复制开发机系统安装。
- 同一精简包沙盒的 `acceptance/result.json` 为成功：离线缺导出依赖时明确拒绝，提供本地组件后完成缓存，再离线复用；移动前后 Editor 各 11 项、CLI 两种作者格式和独立 Player 均通过。
- 两种导出均复制到另一全新、断网且无 .NET 的沙盒；该沙盒只映射两份导出与验证脚本，不映射作者工具或缓存。分别校验 565 个发行文件，运行三局批处理与真实 GUI；`artifacts/dual-export-isolation/result.json` 为两种模式全部成功。正式入口的 Windows GUI 子系统由导出预检验证，Lua 工作进程沿用无控制台启动。

实际验证发现并修复了两个产品问题：Windows JSON 默认 CRLF 导致组件内容身份与 Python 的 LF 标准不一致；深目录中的原生硬链接调用未使用扩展路径，导致缓存投影失败。两者均有新增回归覆盖。验收脚本另修正了 PowerShell 5.1 对短进程退出码的采集方式；失败候选及诊断保留，未计入成功验收。

## GitHub 组件分发：本地实现与发布门槛（进行中）

`component_release.py` 已接入 `release.py publish` 和现有 Action。两种包及原完整 Player 的导出组件同时构成必须验证的依赖集合；组件草稿先回读、再公开，产品草稿在全部引用再次验证后才公开。原 V0.1.0 旧发布路径保留，未修改任何远端附件或 tag。

- 41 项脚本测试通过，含组件发布新增 10 项：两版复用同一组件、幂等重跑、中断恢复、零字节 starter 恢复、附件大小／数量限制、跨 Release 组件消失、哈希冲突、外部缺失来源及新发布命令入口的失败阻止。日志为 `artifacts/launcher-publication-tests.log`。
- 对第三项的实际两个候选 ZIP、两个校验附件和两份清单执行只读发布前校验，40 个组件的归档及所有对象全部通过；`artifacts/dual-verified/publication-plan.json` 记录完整集合。这些是基于 `78855f8` 加工作区实现的本地开发候选，不能替换 V0.1.0，也不能冒充后续提交构建的正式版本。
- 实际验收使用 V0.2.0 与 V0.2.1：前者发布本轮发行基础设施，后者更新说明与诊断文档以验证跨版本复用。两版均从各自已提交源码重新构建；后一个版本只新增变化的产品组件及自己的包／清单，运行时与第三方组件引用固定附件。
- 用户已明确授权推送本轮改动，发布 V0.2.0、V0.2.1 及其固定组件 Release；V0.1.0 保持原样。第 4 项在两个真实版本及重跑验收完成前仍不勾选，模拟测试不作为真实 Release 验收。

### 真实发布进展与重跑问题

- V0.2.0 对应 `14505bc`，首次 Action `38195275926` 成功；V0.2.1 对应 `52ce98b`，Action `38195487271` 发布成功。用户随后明确授权将整个仓库及历史改为公开，已确认仓库 `private: false`。
- V0.2.0 的首次同 tag 重建失败，原产品附件没有被替换。对照首次下载清单与第二次构建清单，差异仅在四个 Native AOT 入口；本地强制重新链接复现了三个 PE 时间戳位置的差异。加入原生链接器 `/Brepro` 后，强制重新链接两次得到相同 SHA-256；回归脚本为 `scripts/test-native-repro.ps1`，实测结果见 `artifacts/native-repro-regression/result.json`。
- 发布器新增已公开产品的前置比对，拒绝冲突时不再先发布候选组件。旧 tag 的源码与附件保持原样。新增读取权限的公开版本重验流程，依据原 tag 和原始附件验证完整依赖、匿名下载及发布调用幂等性；该流程的成功结果尚待取得，不将旧 tag 的重建失败记录改写为成功。
- 本机直连 GitHub 下载发生 TLS 超时；已匿名取得 V0.2.0 精简清单，其 SHA-256 与 Release 元数据一致。完整下载验收尚未结束，不将不完整 ZIP 当作合格发行物。

### 原始公开附件重验（2026-10-12）

读取权限工作流 [38197957403](https://github.com/silevilence/Board-Game-Builder/actions/runs/38197957403) 成功：在新的 GitHub Windows runner 上匿名下载两版原始用户包、校验附件、完整清单与组件；验证后使用各自准确 tag 源码执行发布调用重放，两个 `publicationReplay` 均为 true。该重放检查已公开附件的幂等性，不表示前述旧 tag 原生重建成功。未来构建已加入 `/Brepro` 及强制重新链接回归。

| 实际 Release | 独立 ZIP 字节 | 精简 ZIP 字节 | 组件下载字节 | 已有附件复用数 |
| --- | ---: | ---: | ---: | ---: |
| [V0.2.0](https://github.com/silevilence/Board-Game-Builder/releases/tag/V0.2.0) | 77,853,052 | 31,116,858 | 89,138,717 | 0 |
| [V0.2.1](https://github.com/silevilence/Board-Game-Builder/releases/tag/V0.2.1) | 77,854,110 | 31,117,821 | 19,332,691 | 37 |

两版各自包含导出依赖的闭包为 40 个组件，合并为 43 个；第二版只新增 3 个产品组件。37 个运行时／第三方组件复用相同附件 ID、固定来源和哈希。早期失败的旧 tag 重建额外公开过 2 个未被这两版清单引用的候选产品组件，保留原样；发布器的新前置比对避免后续同类冲突先产生候选附件。

原始附件及逐项证据下载到 `artifacts/release-acceptance/verified-downloads/`。证据归档 329,676,598 字节，SHA-256 为 `aa98d16c3bba46ef18a84122082d52c47346a26d8d261a85f205e10a87124b0d`；本机再次对两套用户包及完整组件执行 `component_release.prepare`，全部通过。各版 `verified.json` 记录原始产品附件 ID、哈希、组件附件 ID、请求与传输字节。

再次读取 V0.1.0 Release：产品提交仍为 `1f139e231124d8768f7ed4e711d04f2ce9d00e2d`，三个附件的 ID、名称、大小、digest、更新时间与发布前快照完全相同；证据为 `v010-before.json` / `v010-after.json`。

V0.2.0 原始独立 ZIP 在断网、无源码／SDK／系统 Runtime 的 Windows Sandbox 验证成功，路径含中文及空格，移动前后真实 Editor、CLI 作者操作、正式导出与 Player 均通过。记录为 `artifacts/release-acceptance/V0.2.0-standalone-sandbox/acceptance/result.json`，ZIP SHA-256 为 `3c7b713bd6c10186552730deb5307772a08b32c4cf3cc43a3f763c90ee8a51f7`。

V0.2.1 原始精简 ZIP 在另一断网沙盒完成缺核心、x86、旧补丁、缺桌面框架四类拒绝，以及兼容核心成功检查；安装官方已校验 Core／Desktop 10.0.12 后，SDK 列表为空。移动前后 Editor 各 11 项、CLI 作者操作、缺导出依赖时拒绝、完整组件恢复与离线复用、正式导出和 Player 全部通过。记录为 `artifacts/release-acceptance/V0.2.1-slim-sandbox/runtime-checks.json` 和 `acceptance/result.json`；ZIP SHA-256 为 `306eb3cf70c20baedfdf17130f4a7650f7f3e9cf496e77772af4e6e886561103`。

第 4 项通过；后续启动器共享安装及最终双版本闭环仍按各自任务验收。

## 独立启动器与本地登记（2026-10-12）

新增独立版本 0.1.0 的 Launcher.Core、Avalonia Launcher 与 `bgb-launcher` CLI，只引用独立启动器契约，不依赖任何引擎安装。自包含输出由 `scripts/publish-launcher.ps1` 生成；用户设置单独持久化，登记前校验完整清单、入口与内容，拒绝冲突、未知格式和不安全路径，明确标记目录缺失与损坏。

- `tests/Launcher.Tests` 通过清单拒绝、V/v 规范化、两版登记／默认／重启、重复登记、冲突、损坏／缺失和旧格式登记检查；实际发布的完整／精简清单均解析成功。构建无警告，项目依赖边界检查通过。
- `artifacts/launcher-registry-sandbox/result.json`：断网、没有引擎或系统 .NET 的新沙盒中，CLI 与 GUI 均可启动；程序目录移动到新的中文及空格路径后继续启动，设置保持不变，前后截图已检查。
- 原始 V0.2.0、V0.2.1 独立包在 `artifacts/release-acceptance/registry-products/` 解压后同时登记。新 CLI 进程读取设置、选择 `v0.2.1` 为默认并重新校验，两个版本均为 available；记录为 `registry-status.json`。GUI 的 `registry-gui.json` 与截图显示两个独立版本及准确默认值。
- 最新自包含构建位于 `artifacts/launcher-registry-final/`；`docs/launcher.md` 说明独立设置、版本运行缓存、存储位置和手动更新。当前登记不宣称已实现共享安装，后续任务继续补充。

## GitHub 版本目录与离线列表（2026-10-12）

启动器与 CLI 使用统一的 Release 目录，分页扫描固定官方仓库，验证两种产品清单、必需附件和跨 Release 组件来源；排除草稿、预发布、组件 tag、源码归档及其他平台。以数值版本排序，规范化重复来源作为冲突显示。缓存仅在整次读取成功后原子更新，网络失败与限流保留原列表；本地登记独立于远端存在性。

`artifacts/launcher-catalog-tests.log` 通过历史分页、V0.9.0／v0.10.0 排序、重复版本、离线缓存、组件失效、远端移除和取消测试。实际匿名调用官方 GitHub 得到 V0.2.1、V0.2.0、V0.1.0 三个产品，新格式均识别 standalone／slim，旧版识别 legacy，所有依赖来源有效；45 个组件 Release 没有进入产品列表。完整结果在 `artifacts/launcher-catalog-live.json`。GUI 从该缓存展示产品与已登记状态，启动无需联网。

## 按内容共享存储与实际组件下载（2026-10-12）

`ContentStore` 校验固定组件归档及每个解压对象，以 SHA-256 寻址只读内容，通过 NTFS 硬链接投影版本目录。安装与内容回收共用跨进程存储锁；失败／取消删除本次新对象及传输临时文件，重启后清理未完成传输。可用空间、固定本地 NTFS 和同卷链接能力在写入前检查。

[真实在线验证 38201658857](https://github.com/silevilence/Board-Game-Builder/actions/runs/38201658857) 在 Windows runner 成功，使用启动器实现直接匿名请求 GitHub 固定组件附件，不下载完整产品 ZIP。原清单来自 V0.2.0 与 V0.2.1，验证包含内容哈希与每个版本文件的实际 NTFS 文件 ID。

| 实际安装 | 组件 HTTP 请求数 | 实际传输字节 | 复用对象 | 新对象 |
| --- | ---: | ---: | ---: | ---: |
| V0.2.0 私有模式 | 38 | 77,712,762 | 0 | 407 |
| V0.2.1 私有模式 | 1 | 7,904,831 | 377 | 30 |

第二版仅请求变化的产品组件，运行时及第三方组件请求为零。两版 4,256 个逻辑文件共 1,986,939,908 字节，实际共 437 份物理内容、206,724,123 内容字节，NTFS 分配 207,523,840 字节；核心／桌面 Runtime 各一份物理文件。该测量不包含目录元数据、各版顶层清单和可写缓存。证据在 `artifacts/launcher-store-ci/shared/measurements.json`、`physical.json` 和 `artifacts/launcher-store-action.log`；下载的证据 ZIP SHA-256 为 `0d3cebcf55eab57059bec178d0a8510675ccb458ced38927209b1b52078bcc65`。

本地实际组件归档传输替身得到相同物理结果，另通过损坏归档、损坏已存对象、并发复用、取消、空间不足与网络存储拒绝测试。本机直接下载尝试在 15 分钟限时取消，已清理新对象且没有安装登记，未作为成功在线证据；实际在线成功来自上述 runner。第 7 项通过，版本安装登记事务由第 9 项继续验收。

## 私有／系统 Runtime 策略（2026-10-12）

启动器通过包模式选择固定私有或系统框架，检查候选与已安装版本，并显示每个程序的声明版本、实际版本、位置和兼容策略。系统解析直接编译共享原生入口的 `SystemRuntime.cs`，避免启动器与直接入口出现两套判断；此复用没有引入引擎／Editor 项目引用。私有框架依据完整清单的准确 runtimepack 归属和文件哈希检查，新增内容身份不覆盖其他版本。

- `artifacts/launcher-runtime-policy-tests.log`：同一解析器的缺框架、错误架构、旧补丁／跨主版本拒绝、兼容较新补丁、Desktop 对 Core 依赖以及仅对子进程设置环境测试通过。
- `launcher-runtime-private.json` 与 `launcher-runtime-system.json` 分别记录真实 V0.2.0 私有 Runtime 及 V0.2.1 系统框架解析，实际 Core／Desktop 为 10.0.12。第 4 项的两个独立沙盒已覆盖无 SDK、无 Runtime、仅兼容 Runtime、直接 Editor／CLI／Player 与工作进程。
- 为父进程设置无效 `DOTNET_ROOT`／`DOTNET_ROOT_X64` 和 `Major` 滚动策略后，直接启动导入后的 V0.1.0、V0.2.0、V0.2.1 CLI，三者均返回自己的版本与文档。记录为 `artifacts/launcher-runtime-direct-*.json`，测试结束恢复当前进程变量，没有修改系统配置。
- `artifacts/runtime-side-by-side/result.json`：两个受控 .NET 程序同时使用独立官方 10.0.11 和 10.0.12 Runtime 目录运行，实际进程分别报告对应补丁；这验证不同 Runtime 可并存，不冒充两个产品 Release 声明不同 Runtime。真实两版产品均锁定 10.0.12，并已验证跨版本只存一份。

第 8 项通过；共享安装事务、启动／卸载保护仍在后续任务验收。

## 安装事务与 ZIP 导入（2026-10-12）

安装持有共享存储事务锁，在私有暂存目录恢复内容、建立硬链接并校验全部文件与运行时，然后原子移动并登记。日志记录准确版本及清单身份，重启恢复未登记目录；清理只处理受控存储路径，删除只解除当前硬链接，不改变其他版本文件属性。导入锁定已校验 ZIP 的读句柄，拒绝不匹配校验文件、重复／越界路径、外部链接、未知清单、缺失文件与超限归档。

- `artifacts/launcher-install-tests.log`：清单边界、损坏 ZIP、越界、取消、同版本并发导入、旧版保留、原子目录移动后中断恢复和恢复后重试通过。
- [真实在线／离线等价验证 38202260899](https://github.com/silevilence/Board-Game-Builder/actions/runs/38202260899) 成功：从官方目录解析两版并以实际 HTTP 组件安装；另匿名下载原始独立 ZIP 并导入，逐版比较清单身份及全部内容，两条路径等价，物理统计均为 437 份内容和 207,523,840 分配字节。证据在 `artifacts/launcher-install-ci/` 与 `launcher-install-action.log`；证据 ZIP SHA-256 为 `9fde4780ba637fdaf353eedf6270653519e1c9ca85a725f966e2cce2a9eb189c`。较早的工作流因本地检查发现 PowerShell 数组拼接问题而取消，修正后使用此成功运行，不计入成功记录。
- `artifacts/launcher-import-real/` 在本机独立导入相同两版，再导入原始 V0.1.0（ZIP SHA-256 `4cde7c2cf61c90cf862270c1f9c03794cf88969cef0e9076d8b891018ed978ea`），登记为 legacy，保留原布局；三版直接 CLI 都返回各自版本及文档。V0.1.0 的清单 digest 与官方原附件完全一致。
- `artifacts/launcher-import-slim.json` 记录 V0.2.1 原始精简 ZIP 导入成功，模式为 slim；兼容系统 Runtime 下直接 CLI 成功，记录为 `launcher-import-slim-direct.json`。

第 9 项通过。安装 GUI 具备状态、取消、模式选择、ZIP 选择和启动时恢复；运行保护、卸载、共享修复及最终干净系统中的启动器闭环继续由后续任务验收。

## 启动、共享修复与卸载（2026-10-12）

第 10 项通过。`scripts/validate-launcher-windows.ps1` 在无源码／SDK／系统 Runtime、断网的 Windows Sandbox 使用原始 V0.2.0、V0.2.1 独立附件及原始 V0.1.0 ZIP，启动器、设置、存储均放在中文含空格路径。结果为 `artifacts/launcher-maintenance-sandbox3/result.json`，GUI 图像为同目录 `launcher-gui.json.png`（已检查）。

- 两个实际 Editor 同时显示窗口，操作系统检测同时覆盖原生入口和托管进程；更改默认不影响它们。正在使用时卸载和共享修复都被拒绝。
- 受控破坏一个共用文档对象，停止两版后从原组件归档读取 7,903,837 字节，校验替换一个对象并恢复两版。记录的是本地归档读取，未冒充网络下载。
- 再启动 V0.2.1 后卸载 V0.2.0，前者继续运行并可调用 CLI。437 个物理对象降至 407，核心／桌面 Runtime 各保留一份，NTFS 分配为 195,014,656 字节。重装与直接入口通过；GUI 重启恢复两版可用状态。最后两版卸载后物理对象为零。
- 原始 V0.1.0 导入、CLI 与卸载通过；用户工程标记完整保留。旧格式使用 ZIP 文件所有权记录，不把旧目录推断为新共享格式。
- `launcher-maintenance-tests.log` 覆盖重新创建启动器实例后的进程保护、共用原生映像、取消默认后的重装、用户文件拒删、独占文件导致部分失败及恢复修复；全部通过。项目边界检查通过。

首次沙盒发现嵌套 CLI 标准流未正确转发，第二次发现后台 Editor 继承了调用方管道；分别修正为 CLI 显式流转发和 Editor 独立 Shell 启动。前两次尝试不计为成功验收；第三次完整通过。安装登记和缓存清单共同保存准确引用，维护使用存储锁、登记锁、操作系统占用检查及持久事务日志；损坏内容以新文件替换，不原位修改别的版本使用的对象。

## 工程作者版本绑定（2026-10-12）

第 11 项通过。独立作者绑定 `author.engine.json` 使用严格 API／字段与原子写入，不修改作者插件 profile 或运行入口。显式版本、工程绑定、默认值依次解析；返回实际版本、来源、绑定值及可用状态。查看、临时打开、写绑定、清除绑定分别是独立操作，缺失／损坏不回退。

- `artifacts/launcher-project-tests.log`：两种格式的绑定模型、子目录定位、临时覆盖、默认变化、缺失版本、未知绑定 API 与游戏字节保留通过。
- `artifacts/launcher-project-real/result.json`：原始 V0.2.0 CLI 分别创建 module／state 工程；绑定旧版后更改默认为 V0.2.1，两者仍由真实 V0.2.0 Editor 打开。通过实际工作区公告、进程路径与启动时间核对版本和工程身份，打开前后入口 SHA-256 不变。临时选择新版返回 explicit 且旧绑定保留；绑定未安装 0.9.0 得到 missing。
- 将测试工程入口 format 改为 999，旧版实际 `project.validate` 返回失败并保留原输入，记录 `incompatible.stdout`；启动器不迁移输入。独立导出隔离运行在最终验收继续复核。

Launcher GUI 与 CLI 构建通过；此项没有修改 Editor 插件、引擎或游戏运行格式。

## 统一 CLI 与固定版本 SKILL（2026-10-12）

第 12 项通过。路由复用工程选择，将一次调用的 CLI、文档、作者操作及运行时固定在选中完整清单；GUI 模式核对当前用户工作区公告、进程启动时间与实际版本目录，匹配后传递明确实例。旧单版本 CLI 保持可用。SKILL 模板和安装维护抽离运行时依赖后由 Launcher 编译同一份源代码，没有引入引擎或 Editor 项目引用。

- `artifacts/launcher-routing-tests.log`：绑定路由、冲突工程拒绝、完整 JSON 参数、无 GUI 时拒绝磁盘回退、两版技能共存／独立修复／卸载，以及取消子进程树返回 130 通过。
- `artifacts/launcher-skills-engine-tests.log`：原作者模板和复制／Junction 安装维护回归通过。
- `artifacts/launcher-routing-real/result.json`：原始 V0.2.0 state 工程与实际 Editor 连接，固定旧版后改默认为新版，CLI 文档仍返回 0.2.0 的提交身份；GUI 校验回执确认为 gui 模式和该工作区。V0.2.1 CLI 连接旧版实例被明确拒绝，见 `mismatch.stderr`。
- 同目录安装两版技能，逐项执行生成元数据中的固定命令，分别返回对应真实版本；删除旧版技能文本后 repair 恢复，卸载旧技能后新版仍 healthy，最后正常卸载两者。记录保存了安装身份、固定命令和独立文档路径。

完整干净 Windows 的作者、导出和隔离玩家验收由最终任务记录。

补充：`artifacts/launcher-stdio-tests.log` 验证嵌套进程的实际标准输入、独立输出／错误流及退出码 37，取消返回 130；手动更新启动器路径后修复固定路由的技能也通过。

## 最终干净 Windows 作者验收（2026-10-12）

使用原始 V0.2.0／V0.2.1 的独立、精简附件，以自包含启动器在两个全新、断网的 Windows Sandbox 完成安装和作者流程。独立环境无源码／SDK／系统 Runtime；系统模式只展开逐个 SHA-512 核对的官方 Core／Desktop 10.0.12，SDK 列表为空。脚本为 `scripts/validate-launcher-author-windows.ps1`。

- 独立模式完整成功记录：`artifacts/launcher-author-standalone-sandbox5/result.json`。
- 系统模式完整成功记录：`artifacts/launcher-author-slim-sandbox/result.json`；实际框架在同目录 `system-runtimes.txt`。
- 每个环境都创建 module／state 工程，分别绑定 V0.2.0／V0.2.1；变更默认后查询、实际 Editor 工作区、CLI 文档提交身份保持一致。GUI 草稿通过 CLI 写入和读回，校验／打包／导出不保存；显式保存后 UTF-8 文本逐字一致。
- 两版实际 GUI 连接跨版本拒绝、固定技能安装／修复／互不影响的卸载、缺失绑定拒绝、入口 format 999 的实际校验拒绝且字节不变，均通过。两个启动器 GUI 截图已查看；记录为各目录 `gui.json.png`。
- 系统模式第一次离线导出缺依赖时拒绝且无半成品；随后由原始固定组件校验补齐，导出每版完整游戏。导出缓存添加未知用户文件时卸载拒绝并保留；移除测试标记后正常卸载两版。两个环境的最终产品对象均归零，工程及已复制的导出物保留。

| 两版安装的实际物理结果 | 私有模式 | 系统模式 |
| --- | ---: | ---: |
| 唯一产品对象 | 437 | 224 |
| 内容字节 | 206,724,123 | 93,492,636 |
| NTFS 分配字节 | 207,523,840 | 93,904,896 |
| 逻辑产品文件字节 | 1,986,939,908 | 499,702,898 |
| 逻辑文件数 | 4,256 | 1,104 |
| 产品内核心／桌面 Runtime 物理份数 | 1／1 | 0／0 |

统计核对真实文件 ID；范围为共享不可变产品文件，不包含启动器、系统框架、可写导出缓存、目录元数据或独立游戏。真实下载量及第二版不下载未变组件的证据见前述两次 GitHub runner 验证，不用这些离线导入量替代网络测量。

沙盒前四次作者尝试不计为完整成功：发现 PowerShell 5.1 空数组枚举、作者 JSON BOM、无 BOM Lua 默认按系统编码读取，以及启动器中文诊断按控制台代码页输出的问题。测试脚本显式处理数组和 UTF-8；发布包作者 JSON 使用无 BOM；启动器自身重定向诊断统一 UTF-8，子工具字节流仍直接转发。第五次独立模式和随后系统模式均从头完整通过。旧发行包的 BOM 校验限制已在用户故障排查中说明，未替换既有 Release。

最终在线复查确认仓库为 PUBLIC。`artifacts/launcher-release-final-check.json` 比较 V0.1.0 原始记录的 Release／附件 ID、提交、大小、SHA-256 及更新时间全部不变，并逐一核对 V0.2.0／V0.2.1 当前公开附件与此前下载的原始文件完全一致。

玩家隔离验收也通过：`scripts/validate-launcher-export-isolation.ps1` 在第三个全新、断网、无 .NET／SDK／作者工具的 Sandbox 中只映射四个已导出的游戏目录。两种模式 × V0.2.0／V0.2.1 均完成三局 CPU、重复与回放验证以及实际图形 Player smoke；入口 PE 子系统均为 GUI。没有映射启动器、下载目录、工程或共享存储。结果、游戏 SHA-256、四份 GUI 报告和逐局记录在 `artifacts/launcher-export-isolation-sandbox/`，总结果 `result.json` 的四项全部成功。

第 13 项通过。

## 整组审核与最终回归（2026-10-12）

以首项实现前的 `5ba0887^` 为起点审核整组发行协议、组件发布、共享存储、安装维护、运行时选择、工程绑定和作者路由。最终发现并修复了一个中断恢复边界：版本目录已就位但尚未登记时，恢复／回滚现在同样检查直接运行的进程及新增用户文件，拒绝清理并保留事务；安装日志先写临时文件并刷盘，再原子提交，未完成日志不会阻塞后续恢复。针对真实运行进程、用户文件和半份临时日志的回归全部通过。

- `artifacts/launcher-final-build.log`：整个 `BoardGameBuilder.slnx` Release 构建通过，零警告、零错误。
- `artifacts/launcher-final-python-tests.log`：45 项发行脚本测试全部通过，包含原生入口的固定链接及重复链接验证。
- `artifacts/launcher-final-engine-tests.log`：系统运行时、导出依赖、作者 profile／导出、技能模板及技能安装六组回归通过。
- `artifacts/launcher-review-tests.log`：Launcher 全套目录、存储、安装恢复、进程保护、修复卸载、绑定、CLI／技能路由和登记测试通过；`scripts/check-boundaries.ps1` 通过。
- `artifacts/launcher-delivery-smoke2/result.json`：最终恢复修复构建的自包含启动器导入原始 V0.2.0，实际 CLI 返回准确版本；模拟未登记安装并添加用户文件，恢复拒绝且保留文件，移除测试文件后恢复成功；重新导入与卸载成功，最终共享对象为零。第一次补充脚本误传文档主题 `--json`，改为已有协议的 `docs entry` 后重新完整验证，未修改发行包。

13 项均已验收，无剩余阻塞。完整干净环境作者／隔离玩家矩阵使用最终恢复修复之前的构建；恢复修复之后执行上述针对性回归和自包含真实附件检查，没有将两者冒充同一次验证。V0.1.0 及已公开 V0.2.0／V0.2.1 标签、附件保持不变。启动器版本独立为 0.1.0，使用 `scripts/publish-launcher.ps1` 交付 Windows x64 自包含目录；不需要系统 .NET，首期通过替换程序目录手动更新。
