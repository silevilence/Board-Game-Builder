# 发布 CLI 使用说明

V0.4.0 使用当前工具包根目录中的 `bgb.exe`。例如解压至 `C:\Tools\Board-Game-Builder-V0.4.0` 后，将下方 `$bgb` 设为该目录的绝对路径；旧版本目录只用于记录历史创作证据。

本例先编译当时最新源码的完整本地发行物，再使用其中 `bgb.exe` 创作和验证。CLI 没有新增命令；作者领域能力通过既有插件操作发现机制提供。

## 发行物

本地 `0.2.1 / win-x64 / API 1`，不是新增公开 Release。最初基线来自 `15d019c`，随后补齐字体、动画、分页文档并重建；标题界面追加交付使用 `artifacts/cursed-investigators/title-v11`，含 CLI、Editor、worker、正式／调试 Player、模板和作者文档。`toolkit.json` 记录基础提交，未提交修改的精确源码摘要另存于验收证据 `source.json`，不能只凭版本号区分。

前几轮 ZIP 使用 `scripts/publish-toolkit.ps1`；后续采用 [完整目录构建脚本](../../docs/validation/evidence/cursed-investigators/animations/publish-local.ps1)，仍重新编译所有组件并运行官方 `validate-toolkit.ps1`。没有用单项目 DLL 代替发行物。

## 实际命令

| CLI 命令／操作 | 用途 |
| --- | --- |
| `operations`、`plugins`、`describe`、`docs` | 检查发行版能力、参数及插件协议 |
| `call project.create.state` | 建立状态定义工程 |
| `call files.write --args-file ...` | 通过作者编辑模式保存状态、规则、流程、资源和呈现配置 |
| `call animation.create/read/replace/validate` | 探针验证动画作者链路、版本冲突及共享校验 |
| `validate` | 检查插件、文件、资源引用、流程、初始动作及加载 |
| `run-cpu` | 有步数上限的对局、同种子重复和权威日志回放 |
| `pack`、`export-player` | 游戏包及包含正式 Player 的独立导出 |

命令在前，`--project` 等选项在后。以下从仓库根目录执行；导出目标必须不存在：

```powershell
$bgb = 'C:/Tools/Board-Game-Builder-V0.4.0/bgb.exe'
& $bgb operations --project samples/cursed-investigators
& $bgb describe files.write --project samples/cursed-investigators
python samples/cursed-investigators/tools/build-project.py .scratch/cursed-edits.json
& $bgb call files.write --project samples/cursed-investigators --args-file .scratch/cursed-edits.json
& $bgb validate --project samples/cursed-investigators
& $bgb run-cpu --project samples/cursed-investigators --args '{"seed":1,"count":1,"limit":2400}'
& $bgb export-player --project samples/cursed-investigators --args '{"target":"artifacts/my-investigator-player","offline":true}'
```

编辑前关闭正在使用工程脚本的试玩实例。实测在对局持有 Lua 文件时收到 `write.partial`；CLI 返回已写路径，关闭实例后重试成功，没有把部分写入误报成功。

## 修复和重建

- CLI 原有领域冲突误返回退出码 1，修复为协议要求的 3，重建后以实际 CLI 复验。
- 引擎缺少随包中文字体、动画作者／运行、分页状态文档，分别新增通用内置能力，具体规则和界面内容留在 sample。
- 实际窗口发现字体将换行当作缺失字形，修复多行布局并补真实字形测试。
- 实际导出发现整个游戏包误套用 32 MiB 单资源限制；保留包预算和篡改检查，修正导出尾部校验并重建复验。
- 标题界面追加使用通用 Player 分步配置、下方合法动作区及终局重开入口。重建完整 v11 后，以 `files.write` 保存 sample 配置、`validate` 校验、`export-player` 导出到 `artifacts/cursed-investigators/player-title-v11`，没有新增 CLI 命令或改变对局规则。

实际命令返回、规则／画面验证和独立导出证据见 [验收记录](../../docs/validation/cursed-investigators.md)。

独立运行验证使用既有 Player 入口，自动执行同一套合法真人动作并重开一次：

```powershell
./artifacts/cursed-investigators/player-board-v13/Play.exe gui --smoke .scratch/investigator-player-check.json --actions docs/validation/evidence/cursed-investigators/sample/winning-actions.json
```

它会完成两局后关闭窗口并写入结果；正常游玩直接双击 `Play.exe`。标题版包的运行指纹为 `D266D5E4C7496A93F491A1EE42383E9D98C10884B5F5C4CF02E82EF8A96FBDFE`；上一版 v9 的证据仍保留为历史验收。

## 牌桌直接操作追加

本次重新编译完整 `artifacts/cursed-investigators/board-v13`，通过其 `bgb.exe call files.write` 应用牌桌区域、规则展示和提示文字；执行 `validate` 后，以 `export-player` 导出到 `artifacts/cursed-investigators/player-board-v13`。通用区域和局部菜单由内置 Player 实现，sample 只提供布局与合法动作绑定。没有新增 CLI 功能。

既有独立 Player `--smoke` 验证入口在配置牌桌区域的模块中改为通过真实桌面坐标点击，包括菜单展开及翻页；开局选项仍经当前合法动作提交。详细结果见 [牌桌操作验收](../../docs/validation/cursed-investigators-board.md)。

牌桌版 v13 的最终运行指纹为 `6F7BF5F1052DB8444399BF855CF1AE47A7EFFBA19AC4FF0A037E413952BB16EA`；独立正式 Player 连续两局获胜，1116 次牌桌点击和 4 次开局动作，最终状态一致。
