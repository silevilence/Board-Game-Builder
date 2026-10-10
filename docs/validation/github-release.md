# V0.1.0 GitHub 自动发布验收

2026-10-10。对应 ROADMAP「CLI 完成后的 GitHub 自动发布」。

## 发行布局与本机验证

- 自包含 Windows x64 工具包包含根 CLI、editor、debug-player、正式 player、每个宿主的 Lua worker、本地作者文档、作者 SKILL 模板及示例。
- Editor 依据安装根的 toolkit.json 定位配套 Player，不依赖当前工作目录。开发目录仍保留原有调试默认值。
- 本机 Release 全解决方案构建 0 警告、0 错误；依赖边界检查通过。
- `author-cli`、`author-templates`、`author-exports`、`author-workspace`、`author-connections`、`author-docs`、`author-skill-install` 七组通过。
- 发行 CLI 的离线文档、操作目录、正式验证和导出通过，导出 Play.exe 的 seed 42–44 三局 CPU 对局及重跑／回放验证通过。
- 工具包移动至 `artifacts/发行 验证/toolkit`，从系统临时目录启动真实 Editor 的 `--verify-toolkit`，新建、编辑、保存重开、自动定位调试 Player、内嵌／外部完整对局、自动定位正式 Player 和独立导出 9 项检查通过。
- 本地证据：`artifacts/release-solution-build.log`、`release-regression.log`、`toolkit-validation/result.json`、`发行 验证/editor-check/editor.json`。这些是开发机证据，不能替代干净环境。

## 发布故障路径

`python -m unittest discover -s scripts/tests -v` 11 项通过：首／中／末条日志精确提取、大小写标题、非法版本、缺失／重复／空内容、版本不一致／缺文件，以及受控 GitHub 传输模拟中的上传中断恢复、重复发布、大小写别名复用、别名指向冲突、已有附件冲突、错误校验和与缺失构建产物。发布前回读所有附件；公开版本不覆盖。

真实 GitHub Action 与下载产物的干净环境验收结果将在实际执行后追加；当前不将这些列为已通过。
