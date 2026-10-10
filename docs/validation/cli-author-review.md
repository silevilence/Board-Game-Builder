# CLI 作者工具验收记录

2026-10-10。范围为 ROADMAP「CLI 作者工具与可替换编辑模式」；审核基线 `55f6597f3aec6ff10712c1ecf268709123ad06d0`。本记录持续补齐实际验收证据，产物位于未跟踪的 `artifacts/cli-author/`。

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
