# V0.1.0 GitHub 自动发布验收

2026-10-10。对应 ROADMAP「CLI 完成后的 GitHub 自动发布」。

## 发行布局与本机验证

- 自包含 Windows x64 工具包包含根 CLI、editor、debug-player、正式 player、每个宿主的 Lua worker、本地作者文档、作者 SKILL 模板及示例。
- Editor 依据安装根的 toolkit.json 定位配套 Player，不依赖当前工作目录。开发目录仍保留原有调试默认值。
- 本机 Release 全解决方案构建 0 警告、0 错误；依赖边界检查通过。
- `author-cli`、`author-templates`、`author-exports`、`author-workspace`、`author-connections`、`author-docs`、`author-skill-install` 七组通过。
- 发行 CLI 的离线文档、操作目录、正式验证和导出通过，导出 Play.exe 的 seed 42–44 三局 CPU 对局及重跑／回放验证通过。
- 工具包移动至 `artifacts/发行 验证/toolkit`，从系统临时目录启动真实 Editor 的 `--verify-toolkit`，图片导入与 Undo、新建、编辑、保存重开、自动定位调试 Player、内嵌／外部完整对局、自动定位正式 Player 和独立导出 11 项检查通过。
- 本地证据：`artifacts/release-solution-build.log`、`release-regression.log`、`toolkit-validation/result.json`、`发行 验证/editor-check/editor.json`。这些是开发机证据，不能替代干净环境。

## 发布故障路径

`python -m unittest discover -s scripts/tests -v` 13 项通过：首／中／末条日志精确提取、大小写标题、非法版本、缺失／重复／空内容、版本不一致／缺文件，以及受控 GitHub 传输模拟中的上传中断恢复、重复发布、大小写别名复用、别名指向冲突、已有附件冲突、错误校验和与缺失构建产物。补充 Windows 正文换行与随机清单键序的重建一致性。发布前回读所有附件；公开版本不覆盖。

## 实际 GitHub 发布

- 2026-10-10 14:56（Asia/Shanghai）正式公开 [V0.1.0](https://github.com/silevilence/Board-Game-Builder/releases/tag/V0.1.0)。tag 与工具包源码提交均为 `1f139e231124d8768f7ed4e711d04f2ce9d00e2d`。
- [Action 38032446978](https://github.com/silevilence/Board-Game-Builder/actions/runs/38032446978) 第一次执行所有步骤成功，ZIP、同名 `.sha256` 及 `toolkit.json` 三个附件全部上传并回读验证后才公开。
- ZIP 名称 `Board-Game-Builder-V0.1.0-win-x64.zip`，462143000 字节，SHA-256 `4cde7c2cf61c90cf862270c1f9c03794cf88969cef0e9076d8b891018ed978ea`。
- 首轮发布曾在创建草稿后立即查询列表失败，未公开且没有附件。改为直接消费创建 API 回执，补充换行核对与稳定清单序列化，再完成正式发布。已发布 tag 不再移动。
- 同一 tag 的第二次 Action 也成功；重新构建后核对所有既有附件，未重复创建 Release 或覆盖附件，实际验证重跑幂等。
- Action 日志、Release 元数据保存在 `artifacts/release-success.log`、`artifacts/release-rerun-success.log` 和 `artifacts/release-published.json`。公开正文与已有 V0.1.0 changelog 条目逐字核对一致。

## 下载包的干净 Windows 验收

使用从正式 GitHub Release 实际下载并核对 SHA-256 的 ZIP；不是本机重新构建的替代包。新建 Windows Sandbox，Windows `10.0.26100.0`，无 dotnet 命令、无 x64／x86 .NET 安装目录，断网且不映射引擎源码。产品先解压到 `C:\发行 验证\Initial Toolkit`，再移动到 `C:\发行 验证\Moved Toolkit`。

- 两个位置的真实 Editor `--verify-toolkit` 均全部 11 项成功：图片导入命令与 Undo、创建、编辑、保存重开、包内依赖定位、内嵌和外部对局、正式导出。
- 离线本地文档可查询；Copy SKILL 安装、移动后修复、状态与卸载成功，Junction 安装、状态与卸载成功。
- 关闭 GUI 后，发行 CLI 完成 `module` 和 `state` 两种工程创建、验证、seed 17 起三局 CPU 重跑／回放与打包；默认正式 Player 导出不要求填写开发路径。
- CLI 导出的独立 Player 完成 seed 42–44 三局批处理，以及两个有实际 action 输入的图形对局，均成功。
- 总报告 `artifacts/release-sandbox-verified/acceptance/result.json` 为 `success: true`，记录同一版本、提交与 ZIP SHA-256；环境、移动前后 Editor、CLI 日志、批处理与图形报告同目录保存。
- 可复现脚本 `scripts/validate-clean-windows.ps1` 兼容系统自带 PowerShell 5.1。首次测试脚本的 `Cli` 函数与该版本内置别名冲突，修正辅助函数名后在全新 Sandbox 重验通过；此修正未改动下载的产品二进制。

## 独立游戏与整体复核

将上述 Editor 导出的、包含导入图片的游戏目录单独交给另一全新 Windows Sandbox；只映射游戏输入及报告目录，复制到来宾 `C:\Game` 后运行。环境报告再次确认无 dotnet、无 x64／x86 Runtime；游戏目录没有 CLI 或 Editor。`scripts/validate-standalone-windows.ps1` 验证 seed 71–73 三局批处理及两局带实际 action 输入的图形对局通过，终态保留 `sample-001` 图片资源。

另以无参数 `Play.exe` 正常启动入口实际查看桌面，在 Player 窗口点击「结束」，确认显示「对局结束」，再经「文件 → 退出」关闭，进程退出 0；启动、对局及退出观察到的是图形窗口，没有控制台窗口。结果在 `artifacts/release-player-sandbox/environment.json`、`result.json`、`gui.json` 和 `batch/`，界面操作截图保留在本次会话。

整体复核：产品路径适配留在现有 Editor 服务，未添加游戏专用分支；版本与 API 分开管理；发布只读取 tag 提交的日志和构建输入，草稿附件完整且回读通过后公开，公开版本不覆盖。正式发布、同 tag 重跑、下载包作者闭环及独立游戏验证全部通过。本组无剩余阻塞，ROADMAP 在原位置勾选，未挪动任务。
