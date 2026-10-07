# 群岛开拓 · M3 总体示例验收

日期：2026-10-07。规格位于 ROADMAP 开发中 M3 末尾。用户确认允许简化卡坦岛规则，以演示引擎为优先。

## 交付内容

- `samples/island-settlers`：四席位完整有限局，19 地块／54 交点／72 条边，纯 Lua 规则与 CPU。
- 三个指定 Kenney 包的 25 张图片与三份原始 CC0 许可；`scripts/import-island.py` 可从原 ZIP 重建模块和资源，不覆盖 Lua。
- 默认 p1、p2 真人，p3、p4 CPU；Player 尊重模块默认席位设置，仍可选择单人、热座或分视角。
- 双骰生产、距离与道路连通、村庄／城市、强盗封锁、资源兑换、隐藏发展牌、7 分胜利或 12 轮最高分结算。
- 可选秘密竞拍，锁定前后隔离，所有人锁定或逻辑截止后揭示，获胜者支付麦获得声望；轮换同价优先权，0 出价不获奖。
- 港口成交统计使用已有受限 WASM counter 的组件／命令，复用版本、哈希、权限和原子命令处理。
- 通用投影视图增加可配置图片尺寸、图层、颜色、线段和属性替换标签；Editor 与 Player 共用。修复长期运行动态文字缓存满后不显示新文字的问题。热座动作改为滚动侧栏，CPU 结束时也公开显示胜者。

## 规则边界

这是卡坦岛规则启发的简化示例，不是官方基础版复刻。公开资源、无限银行、统一 3:1 港口、顺序两轮初始布置、每回合两次建设动作和最少资源补给；不含弃半牌、偷牌、玩家议价、最长道路或最大军队。发展牌为骑士／丰收／工匠三种；详细费用、胜利、竞拍与基础版差异见示例 README。

规则参考：[CATAN 官方基础版问答](https://www.catan.com/faq/basegame)。美术来自用户本地 ZIP，不使用官方 CATAN 美术。

## 自动证据

- `dotnet build BoardGameBuilder.slnx -c Release`：0 警告、0 错误；`scripts/check-boundaries.ps1` 通过。
- `dotnet run --project tests/Engine.Tests -c Release --no-build -- island island-modes local-table freecell freecell-shortcuts view interaction`：通过。
- `island` 覆盖拓扑数量、免费布置、距离规则、越权与非法建造不修改状态、双骰城市生产、强盗封锁、资源计费、WASM 计数、两次行动上限、隐藏牌身份及资源隔离、密封出价隔离、竞拍扣费和防重复结算、关闭竞拍、回合上限并列结算与回放。
- `island-modes`：种子 42 全 CPU 135 条命令、p4 获胜；目录模块的进程内 Play 与模块包的独立 Player 调试会话逐步帧／状态哈希一致，完整命令日志相同，双方记录均回放到同一状态。包解压会改变诊断源文件路径，因此记录元数据不要求字节相同。
- 两名真人控制席位（测试通过其投影和可用动作选择）＋两名 CPU，在热座与分视角各完成一局；换座无旧投影，错误身份、过期输入被拒绝，私有牌无跨席位泄漏，CPU 终局结果可见。
- 空当接龙 20 个种子发牌／重跑／回放与既有 94 命令完整通关、快捷操作通关继续通过。
- 最终模块指纹：`0014B1DB41641E49905D32C3C32BEFE3C6A6036EE01D6448FA9BB96FAE958AB3`。
- 种子 42 最终状态哈希：`4B476DE644AFCA9D70189EC80EEE84B2EDCEAA8F0DFB09A737C11031BC95115B`。
- 种子 1～20 **全部通过**：20 局正常终局，20 次同输入重跑与 20 次全部席位回放比对一致，每局 113～230 条命令。可移植摘要为 [island-results.json](island-results.json)；完整权威日志、诊断和模块快照保存在 `artifacts/island-final-batch/`。

## 实际图形检查与发行

- 使用 cua-driver 在本机 Windows 的实际 Player 窗口加载导出包，确认默认热座和 `p1,p2` 真人设置，完成接手、启航、村庄、道路和 p1→p2 换座；遮蔽期间整个桌面视口被清空。
- 在 1804×1367 和 1400×1000 物理窗口尺寸下检查完整岛屿、费用、分数资源、骰子、发展牌区域与滚动动作栏；棋盘自动适应，仍可缩放和平移。
- 截图：`artifacts/island-release-board.png`、`island-release-road.png`、`island-release-handoff.png`、`island-release-resize.png`。
- 最终交付的 `Play.exe` 已实际加载自身 `game.bgm`、开始热座并接手 p1；最终桌面截图为 `artifacts/island-final-preview.png`。
- Windows x64 自包含发行：`artifacts/island-settlers-windows-final/Play.exe`，包含模块、Lua worker、Wasmtime 与必需运行时；导出预检校验 Windows GUI 子系统、依赖哈希及无 Editor／CordiSharp 开发内容。
- 最终发行版运行既有 `--verify-local` 图形回归，卡牌／秘密拍卖 × 热座／分视角 × 全真人／混合 CPU 共 8 种组合全部完成，有实际渲染帧；换座视口销毁、每场结束图形设备回收通过。结果为 `artifacts/island-local-gui-regression.json`。

本轮未重新执行干净 Windows 虚拟机检查，未声称人工在 GUI 中完成整场岛屿对局。完整终局由实际运行时和两种 Play 自动验证；Editor 配置编辑、图片／TTS 导入和打印插件复用既有能力，未为本示例逐项重新人工验收，也未将 TTS 转换算作本示例已覆盖的玩法能力。

## Player 背景修复（2026-10-07）

用户实际桌面截图发现普通 Player 背景透出后方窗口，先前窗口截图检查遗漏了该问题。原因是加载非经典主题模块时写入本地 `Background=null`，覆盖 Fluent 主题底色。现在清除本地值以恢复主题底色，空当接龙保留经典灰色。

新增实际窗口回归入口 `Player.exe --verify-background artifacts/player-background-release.json`，依次加载 quick-cards → freecell → island-settlers，分别验证浅色和深色主题下背景画刷及窗口完全不透明。修复前第一项以 `background:null` 失败；修复后 Debug 和自包含 Release 的 6 项全部通过，普通模块分别恢复白色／黑色，经典模块保持 `#ffd4d0c8`。边界检查通过。

修复版独立导出：`artifacts/island-settlers-windows-opaque/Play.exe`；旧的 `windows-final` 目录保留，避免影响已打开的对局。模块规则及资源不变。
已启动修复版并在实际窗口中开局、接手 p1，棋盘和动作面板正常显示；截图为 `artifacts/island-background-fixed.png`。
