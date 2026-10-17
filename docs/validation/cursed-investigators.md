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
