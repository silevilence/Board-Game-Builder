# Player 外壳与主题解耦验收

日期：2026-10-07。规格：ROADMAP 开发中 M3 末尾「Player 游戏界面与主题解耦」。

## 行为

- 显式加载模块和捆绑 `game.bgm` 的独立导出默认直接开局，显示通用文件／游戏／查看／帮助菜单；无模块参数且无捆绑包默认显示工具栏、等待开局。
- `gui [module] --shell game|tools` 显式覆盖默认行为。文件菜单可切换工具栏，切换不重开对局。
- `viewTheme=1` 保留经典纸牌视觉，停止控制工具栏显隐、自动开局、动作栏显隐和本地会话选择。
- `player.presentation` 是普通哈希资源，Player 读取标题、纯文本帮助和 `showActions`；不修改 Kernel 数据结构／规则协议。缺省使用模块 ID、通用帮助、显示动作栏。
- F3 开局设置中逐席位勾选真人、其余 CPU，并选择种子／单人／热座／分视角。确认才替换对局；取消不影响状态；无真人、单人模式多真人及非法种子保留对话框并报错。
- 空当接龙标题／帮助迁移到模块资源，群岛使用相同宿主流程。隐藏动作栏时仍显示热座交接和控制席位，可由查看菜单恢复动作。

## 验证

- `dotnet build BoardGameBuilder.slnx -c Release --no-restore`：0 警告、0 错误。后续 Player 小范围调整再次构建通过；自包含发行构建通过。
- `scripts/check-boundaries.ps1` 与 `git diff --check` 通过。
- `dotnet run --project tests/Engine.Tests -c Release --no-build -- freecell freecell-shortcuts island island-modes local-table packages view interaction` 全部通过；包括空当接龙 94 条命令通关、群岛目录／包与两种 Play、混合热座／分视角、合法投影和回放。
- 实际 Avalonia 窗口 `Player.exe --verify-shell artifacts/player-shell-release.json` 通过：freecell／island-settlers／quick-cards × game／tools；主题不改变自动开局和工具栏；模块切换无旧标题／帮助；切换外壳保留会话；设置取消不变更对象／修订号；非法设置不替换会话；p2／p4 真人时 CPU 衔接至 p2 并保持交接遮蔽，接手后有实际渲染；重开保留种子／席位；经典主题允许本地热座；错误加载后恢复；目录／模块包展示信息一致；错误 CLI 参数和展示配置被拒绝。
- `--verify-background artifacts/player-shell-background.json`：两种系统主题 × 三模块共 6 项不透明背景回归通过。
- 自包含发行 `--verify-local artifacts/player-shell-local.json`：卡牌／拍卖 × 热座／分视角 × 全真人／混合 CPU 共 8 组完成对局，产生渲染帧，交接视口和退出设备回收通过。
- 使用 cua-driver 启动两个新导出入口：空当接龙直接进入经典牌桌，群岛自动进入默认热座；接手 p1 后显示棋盘和动作栏，没有加载工具栏。通过 F3 打开开局设置，确认 p1／p2 勾选、p3／p4 CPU，完整按钮可见；取消后回到原始对局。截图：`artifacts/player-shell-island.png`、`player-shell-settings.png`、`player-shell-island-cancel.png`、`player-shell-freecell.png`。

## 独立导出

- 群岛：`artifacts/island-settlers-windows-game/Play.exe`；指纹 `CA271367B1594D947C8A0B99A29F3EE3191E9A7C5BC57EEE461EF2F8D57C20EB`。
- 空当接龙：`artifacts/freecell-windows-game/Play.exe`；指纹 `32E3FCE81D3620823EF643BF1975BB53D1A0BD5FF8D3E719677C5CB69D8D1261`。
- 模块增加展示资源并更新 README，指纹随之改变；规则和 CPU 脚本未改动。先前 20 种子批次仍是对应旧指纹／快照的历史证据，本轮未声称重跑完整批次。新指纹的规则、封包及回放回归见上述测试。
- 没有重做干净 Windows 虚拟机验收。旧导出目录保留；新外壳需运行本轮导出的入口。
