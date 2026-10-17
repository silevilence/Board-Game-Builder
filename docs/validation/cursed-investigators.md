# 咒物调查员验收记录

本轮范围：标准单人、战役、初学者。任务状态以 ROADMAP 原地勾选为准。尚未完成整组验收。

## 字体能力（2026-10-17）

- 基线：源码 `15d019c`，用 `scripts/publish-toolkit.ps1 -Tag V0.2.1` 生成 `artifacts/cursed-investigators/baseline`；本地构建，不是新的公开 V0.2.1 Release。发布 CLI 的操作发现、自描述和 `project.create.state` 可用；启用 `resources.font` 时返回明确的未安装能力诊断。
- 实现：通用 `resources.font` 配置字体与许可证路径；静态 TTF／CFF OTF 校验、运行资源池、快照、打包、MonoGame 文字纹理及 Player 控件字体共同使用随包字节。无引擎游戏专用分支，CLI 未修改。协议见 [随包字体](../packaged-fonts.md)。
- 重建：`artifacts/cursed-investigators/font` 及 `.slim` 为包含本任务修改的完整发行物；两种包均通过发布脚本的导出与 CPU 验证。构建时基础提交仍为 `15d019c`，不能仅以 toolkit 的 commit 字段区分，工作树源码摘要和产物哈希见下列证据。
- 实际作者链路：新 `bgb.exe call files.write` 配置测试工程，`validate` 返回无诊断，`call player.export` 生成 `font-export` 独立目录。没有以源码直接调用替代这些验收。
- 独立 Player：从 `%TEMP%` 启动导出目录 `Play.exe gui --smoke ...`，自动重复开局两次并完成操作，结果一致。字体及许可证位于包内，代码直接从包字节创建字形，不查询系统字体。此验证为本机独立目录，不声称是新的 Windows Sandbox 测试。
- 必要回归：`resource-font`、`resource-plugins` 通过；包含缺件、无效路径／字体、包离开原文件、快照恢复及旧工程无字体泄漏。`tests/View.Tests` 实际加载 Noto Sans CJK SC，检查汉字、数字、标点、重复渲染一致性、清空后重载，以及关闭一个控件不影响另一个控件。

[基线诊断](evidence/cursed-investigators/fonts/baseline-font-validation.json)、[新 CLI 校验](evidence/cursed-investigators/fonts/font-validation.json)、[导出](evidence/cursed-investigators/fonts/font-export.json)、[Player 两局](evidence/cursed-investigators/fonts/font-player-smoke.json)、[源码摘要](evidence/cursed-investigators/fonts/font-source.json)、[产物哈希](evidence/cursed-investigators/fonts/artifacts.json)、[字形渲染](evidence/cursed-investigators/fonts/font-render.png)。二进制发行目录留在本机 artifacts，不提交到源码库。

## 动画作者能力（2026-10-17）

通用 `resources.animations` 库通过对象、触发属性、来源对象、关键帧时间与相对位置／旋转描述动画。`author.animation` 是可释放的 CordiSharp 插件，编辑走工作区字段草稿和版本核对，预览使用独立投影，不运行权威对局。协议见 [动画作者说明](../author/animation.md)。

基线无此能力；完整本地发行目录 `artifacts/cursed-investigators/animation-v3` 包含 CLI、Editor、Lua worker、正式／调试 Player、模板与文档。先前两次完整 ZIP 候选后发现退出码缺陷，最终按 [本地构建脚本](evidence/cursed-investigators/animations/publish-local.ps1) 重建所有组件并运行官方 `validate-toolkit.ps1`，没有复用旧程序集。本次最终候选是自包含目录，未再次制作 ZIP 或公开 Release；源码基础提交为 `bfa41cc`，具体修改以 [源码摘要](evidence/cursed-investigators/animations/source.json) 为准。

发布 CLI 实际执行 `animation.create/read/replace/validate`；新 CLI 复验 [版本冲突](evidence/cursed-investigators/animations/animation-v3-stale.json) 返回退出码 3。既有 CLI 曾将领域冲突错误返回 1，已修复，`author-cli` 回归覆盖配置和模板冲突；未增加 CLI 命令／协议。动画领域操作沿用作者插件发现。

发布 Editor 的 [实测结果](evidence/cursed-investigators/animations/animation-v3-editor.json) 覆盖编辑、字段草稿、应用、Undo／Redo、保存重开、预览、两次插件停启、失败预览及切换文档释放。真实视口截图见 [预览](evidence/cursed-investigators/animations/animation-v3-editor.json.preview-0.png)。作者测试覆盖文件草稿冲突及磁盘模式，错误引用与时序格式由共享校验定位。
