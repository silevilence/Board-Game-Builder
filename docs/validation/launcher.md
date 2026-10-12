# 多版本启动器实施与验收记录

对应 ROADMAP「基于 GitHub Release 的多版本启动器」。未完成的阶段不视为验收通过。

## 完整版本依赖清单（2026-10-12）

实现 `toolkit.json` API 2、实际产物清单生成／验证及按哈希对象生成不可变组件归档，接入完整工具包构建。格式及职责边界见 [清单说明](../toolkit-manifest.md)。API 1 的 V0.1.0 保留明确旧格式识别，不推断共享布局。

- `python -m unittest discover -s scripts/tests -v`：20 项通过（原发布流程 13 项，清单 7 项）。覆盖旧版／未知格式、缺项／多项、Windows 路径冲突、非法依赖、损坏文件／组件、重复 JSON 键、同名不同内容、重复构建及仅产品变化时稳定组件复用。
- 使用既有 V0.1.0 实际完整发行目录的副本生成新清单，原验收目录未修改。副本为 `artifacts/launcher-manifest-toolkit`，组件为 `artifacts/launcher-components`。完整清单、文件与组件逐项验证成功；原目录的旧清单识别成功。
- 实测清单记录 2,113 个文件、8 个宿主、54 个库身份、38 个组件；原文件逻辑总量 1,193,157,312 字节，按 SHA-256 的唯一内容量 289,803,319 字节，组件 ZIP 总量 100,017,171 字节。这是已有包的清单与组件测量，**不是后续新发行布局或物理存储去重的验收**。
- 真实 runtimeconfig 声明 .NET 10.0.12，图形宿主同时需要 Microsoft.NETCore.App 与 Microsoft.WindowsDesktop.App。CLI 与 Lua worker 使用核心框架。运行插件目录从正式 Player 清单读取，第三方版本来自 deps。

组件来源目前是不可变附件的声明，尚未发布。没有修改 V0.1.0 附件或 tag，没有将此次本地生成标记为实际 Release 验收。包内去重、精简包、组件发布、启动器及双版本干净环境闭环仍待完成。
