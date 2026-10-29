# 示例入口与迁移清单

所有可运行示例均以目录中的 `game.start.json` 启动；Player、编辑器、批量验证及导出共用这一入口。`module.json` 只是 `definition.module` 插件选择的定义内容，不再声明包身份、Lua 或 WASM 插件。现有玩法、脚本、资源、帮助和默认席位保持原样。

| 示例 | 定义加载器 | 可选运行能力 | 验证 |
| --- | --- | --- | --- |
| quick-cards | definition.module | Lua 规则／CPU | 20 种子 CPU 终局、重跑、回放 |
| hex-skirmish | definition.module | Lua 规则／CPU | 20 种子 CPU 终局、重跑、回放 |
| freecell | definition.module | Lua 规则／CPU | seed 1 合法通关路径、目录与包 |
| sealed-auction | definition.module | Lua 规则／CPU | 20 种子、秘密提交与揭示 |
| wasm-counter | definition.module | Lua 规则／CPU、WASM | 三次计数命令终局、隔离与权限 |
| island-settlers | definition.module | Lua 规则／CPU、WASM | 20 种子 CPU 终局、重跑、回放 |
| poker-uno | definition.module（4／5／6 人预设） | Lua 规则／CPU、统一 PNG 图集 | 基础规则、本地多人、权威联机、观战／恢复；见 [运行说明](../docs/poker-uno.md) |
| state-minimal | definition.state | 无脚本静态命令 | 最小格式、完成命令、包与回放 |
| flow-minimal | definition.module | flow | 最小流程、等待与恢复 |
| flow-cards | definition.module | flow、Lua CPU | 自动节点、动作与终局回放 |
| cursed-investigators | definition.module | Lua 规则／CPU、字体、动画、分页文档 | 资源、展示及完整规则回归 |
| motion-lab | motion.definition | motion.loop、motion.view、图片资源 | 非桌游实时输入、固定时钟、多视口和独立发行 |

11 个桌游示例显式声明 kernel、session.local、recording 和 board.render；motion-lab 不装配桌游能力。作者插件选择独立放在 author.profile.json，不进入运行包。`tests/Engine.Tests` 的 `application-migration` 验证迁移清单、目录／包和作者依赖；`application-regression` 比较新旧完整语义；`freecell-modes` 等复查两种试玩与原验收路径。

`Editor.SamplePlugin` 是编辑器扩展，不是游戏运行插件；`tts-subset` 是导入来源，不直接作为游戏启动。导入使用新建游戏模板，再通过作者格式接口写入实体／资源，生成的游戏同样可试玩和导出。资源生成脚本共同使用 `scripts/runtime_startup.py` 输出新入口，避免重建资源后退回旧格式。

旧作者目录请执行 `Player migrate <旧目录> <新目录>`，再 `Player pack <新目录> <新文件.bgm>`。旧 `.bgm`、无身份快照与旧录制不自动转换；参见 [兼容策略](../docs/runtime-plugins.md)。
