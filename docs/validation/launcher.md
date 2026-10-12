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
