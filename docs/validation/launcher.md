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
