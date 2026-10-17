# 发布 CLI 使用说明

本例先编译当时最新源码的完整本地发行物，再使用其中 `bgb.exe` 创作和验证。CLI 没有新增命令；作者领域能力通过既有插件操作发现机制提供。

## 发行物

本地 `0.2.1 / win-x64 / API 1`，不是新增公开 Release。最初基线来自 `15d019c`，随后补齐字体、动画、分页文档并重建；最终目录 `artifacts/cursed-investigators/final-v9` 含 CLI、Editor、worker、正式／调试 Player、模板和作者文档。`toolkit.json` 记录基础提交，未提交修改的精确源码摘要另存于验收证据 `source.json`，不能只凭版本号区分。

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
$bgb = './artifacts/cursed-investigators/final-v9/bgb.exe'
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

实际命令返回、规则／画面验证和独立导出证据见 [验收记录](../../docs/validation/cursed-investigators.md)。

独立运行验证使用既有 Player 入口，自动执行同一套合法真人动作并重开一次：

```powershell
./artifacts/cursed-investigators/player/Play.exe gui --smoke .scratch/investigator-player-check.json --actions docs/validation/evidence/cursed-investigators/sample/winning-actions.json
```

它会完成两局后关闭窗口并写入结果；正常游玩直接双击 `Play.exe`。规则包的运行指纹为 `A40916F494DA6A1BF74382558B7367345860D286A3BE0B0258A26A1C3E2A1BA9`。
