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
