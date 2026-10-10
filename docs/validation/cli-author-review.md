# CLI 作者工具验收记录

2026-10-10。ROADMAP「CLI 作者工具与可替换编辑模式」19 项均完成验收；审核基线 `55f6597f3aec6ff10712c1ecf268709123ad06d0`。产物位于未跟踪的 `artifacts/cli-author/`，最终产品目录为 `author-final`。整体审核无本组剩余阻塞，既有 Debug WASM 隔离进程崩溃限制单列如下。

## Windows 产品与安装

- 使用 `scripts/publish-author.ps1` 生成自包含 win-x64 CLI，复用已验证的 `artifacts/hyperv-20261009/player-final-release`；CLI、worker、Player 分开发布目录避免 apphost 重名。
- Windows 10 Pro 来宾 DESKTOP-BA23QVD，`dotnet --list-sdks` 为空。测试根 `C:\Users\gdqq\Author Acceptance 20261010` 包含空格。来宾已有 .NET 运行时，本记录不将环境描述为“未安装任何运行时”。
- Copy 与 Junction 分别安装、重复安装升级、移动产品目录后修复、查询、卸载成功，无未解析变量，无关技能保留。两种定义格式均创建、直接改 Lua、读回、校验、固定种子 17/18 重跑回放、正式打包；独立导出 Play.exe 批量运行通过。
- 证据：`guest-result.json`、`guest-author.log`；来宾 `result.json` 和 `player.log`。产品包含自身运行依赖，不需要安装 SDK。
- 宿主 `author-skill-install` 回归还覆盖中文、空格、引号、美元符、反引号路径，外来技能冲突及用户修改保护；`author-skill-template` 覆盖缺失和未知变量、不修改源模板。
- 实际 harness：Codex CLI `0.162.0-alpha.2`，项目 `.agents/skills`。复制技能用于独立游戏创作，Junction 用独立会话发现技能并执行 `docs entry`，证据 `junction-harness.jsonl` 及验收目录 `junction-workspace/discovery.json`。旧 npm CLI `0.147.0` 被服务拒绝当前模型，未将其计为通过。新 CLI 的 Windows sandbox 初始化有环境错误，日志记录其经审批执行后的实际调用；这不等于所有 harness 或 sandbox 配置都已支持。

## 实施边界

发布模板是 `author-skill/bgb-author/SKILL.md.template`，仅包含作者入口。生成的 SKILL 使用实际安装路径，不携带本仓库 AGENTS、ROADMAP 或 editor-cordis。多文件磁盘写入明确区分部分失败与未知，不承诺与任意外部编辑器的全局原子性。GUI 连接中断不会静默切换到磁盘。

## 磁盘作者回归

`tests/Engine.Tests` 新增 author-contract、author-profiles、author-operations、author-disk、author-recovery、author-templates、author-resources、author-validation、author-runs、author-exports、author-docs、author-skill-template、author-skill-install 分组，均实际通过。证据 `full-tests.log`、`review-tests-release.log` 和来宾结果。

- 两个定义加载器都从空目录创建，支持自定义定义路径、直接改文件、资源导入、校验、正式包及运行。卡牌与六角固定 seed 11/12 重跑和回放一致，步数限制作为失败返回。
- profile 选择、可选插件缺失、插件失败反向清理、操作调用期间保留生命周期、参数及取消、目录枚举冲突、新增目标冲突、目标与依赖文件变化均覆盖。
- 多文件故障包含第二文件写入抛错、真正子进程退出后核对和恢复、部分写入后外部再次修改拒绝覆盖、同请求不重复和不同输入拒绝。私有日志不进入正式包。
- 调试会话保留原始输入，源脚本改变后旧会话仍按原输入推进；非法动作和过期步数拒绝，录制可重放。整体审核补充重复请求步数核对、CPU 诊断日志及动作查询错误的同次响应诊断。
- Release 解决方案构建 0 警告／0 错误，`scripts/check-boundaries.ps1` 通过，已加入 Authoring/Cli 依赖白名单。长回归占用 Debug 程序集时一次并行重建失败，改为 Release 输出后验证通过；该构建冲突不计作测试通过。

## GUI 工作区与生命周期

`author-workspace`、`author-connections`、`author-external` 通过，`review-tests-final.log`、`review-followup.log` 保存审核后的回归结果。覆盖磁盘／GUI 相同变更的内容版本相等，文件草稿及字段草稿、无效 JSON、Undo/Redo、显式保存、排队取消和排队期间切换文档、实例歧义、旧代次拒绝、命名管道丢失响应后核对和不重复提交。

真实 Avalonia Editor 入口：`Editor.exe --verify-author <新报告路径> <bgb.exe绝对路径>`。最终报告 `gui-final.json` 的 14 个检查全部通过：挂载文件面板后 CLI 读取草稿、图片导入、完整 Undo/Redo、快照校验与打包不保存、显式保存、外部重载、合并输入跨面板重建、三轮监听移除和重新启用、无结构化作者支持格式的通用输入、插件 effects 归零。未把纯模型测试当成真实窗口验证。

人工合并测试包括无效 JSON 与未应用字段，要求提供最终文件后双版本核对再写入。合并输入使用独立表单基线，文档 Changed 会同步控件；CLI 协调清空草稿后，面板仍能继续输入。面板本身不拥有唯一一份作者文本。

## 独立 Agent 创作

实际使用 Codex CLI `0.162.0-alpha.2` 的全新 ephemeral 会话，工作目录在仓库外的 `%LOCALAPPDATA%\Temp\bgb-author-accept-20261010\workspace`。输入仅含已安装 `bgb-author` 技能和“玩家与 CPU 轮流掷六面骰、先到 20 分获胜”的游戏需求，未提供源码或仓库设计知识。使用当前配置模型，未为降低能力替换模型。

Agent 从本地 docs、plugins、operations、describe 发现接口，创建“掷骰竞速”工程，直接写 Lua 规则和 CPU，诊断缺失脚本及不合法骰点初值并修复，完成 10 步显式调试和幂等重试、录制、正式包及独立 Player。CLI 与导出 Player 各 seed 20261010–20261029 共 20 局全部结束，重复／回放通过，逐种子步数和终态哈希一致；真人输入五次动作后 CPU 自动推进也已实测。没有声称完成该新游戏的图形画面人工验收。

创作所用 CLI 版本 `1.0.0+9bfaf205522ccd0445b98455bc551ac4d311f926`，Player `0.3.5`。游戏运行指纹 `95FA9D08F2D4B151C5AB9453CD5514D5159C9A2428439C86DC0ED58E31009276`；完整输入、调用、失败诊断、修复、哈希与产物报告保存在 `artifacts/cli-author/agent-acceptance/acceptance.md` 和 `evidence/`，会话证据为 `agent-acceptance-v2.jsonl`。

仅复制 Agent 导出的 Player 目录到无 SDK Windows 来宾，以 Play.exe 实际进程退出码核验 seed 42/43/44：9/10/11 步全部结束，重复和回放相同，退出 0。证据 `agent-guest-final.log`、`agent-game.zip`。此运行没有调用 CLI 或 Editor。最初远程脚本未等待 GUI apphost，不能据空 LASTEXITCODE 判定失败或成功；最终改用 `Start-Process -Wait -PassThru` 并读取正式 summary。

## Standards

整体审核发现并修复：同步通知期间恢复草稿造成 Undo 重复键；合并文本只留在控件；CLI 协调后未同步表单；通用合并输入错误依赖结构化格式。补上模型及真实窗口回归后，复核无剩余阻塞。

## Spec

整体审核发现并修复：无效草稿无法提交人工完整合并；调试重复请求未核对 step；CPU／调试未导出诊断，以及动作查询出错后本次响应遗漏刚产生的诊断。补上专项回归后，复核无剩余阻塞。

## 最终构建与回归限制

- 最终 Release 全解决方案构建 0 警告、0 错误，依赖边界检查通过。`final-build.log`、`final-author-tests.log`：全部 16 个作者分组在 .NET 10.0.12 下通过，包括独立 Player 导出后实际运行。真实 Editor 最终 14 项通过。
- 默认长回归完成前 80 组，随后 Debug 的既有 `wasm-isolation` 越界探针原生退出（0xC0000409）。单独 Debug 重跑也复现；host trace 确认实际加载 .NET 10.0.12，不能将本次归因于旧运行时。相关 WASM 实现／夹具及公共编译配置与本组基线相同。
- 使用明确的 .NET 10.0.12 运行 Release 测试程序集，`wasm-isolation`、`image-import`、`tts-import`、`print` 四组全部通过（`tail-regression.log`）。因此 84 个默认分组均有通过结果，但不声称默认 Debug 全量命令一次完成，也不声称已修复该原生崩溃。发行验证采用自包含 Release 产品，本组未修改 WASM 隔离策略。
