# 空当接龙单人验收（2026-10-06）

对应 ROADMAP「空当接龙：真实单人游戏补充验收」。本机开发与独立交付验收通过；本轮没有重新在干净 Windows 虚拟机中运行，不将其记作通过。

## 交付

- 模块：`samples/freecell`，标准 52 张、8 列、4 空当、4 收牌区；规则位于 `rules.lua`，没有 FreeCell 专属 C# 规则。
- 资源：用户提供的 `E:\silev\Downloads\kenney_playing-cards-pack.zip` 中 52 张原始大尺寸 PNG，保留 Kenney CC0 许可。未修改源 PNG；渲染时排除透明边缘。
- 独立 Windows x64：`artifacts/freecell-windows/Play.exe`，自带 .NET 运行时、Lua worker 与 `game.bgm`。附 `玩法说明.md`。没有 Editor 插件；PE 子系统为 WindowsGui。
- 双击后选择种子，点击「开始 / 重新开局 → 洗牌 → 发出 52 张明牌」。种子 1 有完整通关证据。本引擎种子不等于微软牌局编号。
- 模块版本 1.0.0；此次导出指纹 `B8065F92C65CCA0E43DD61F53389D8ADDCF3DDD5E6A90782A09E610401581085`，完整版本信息位于导出目录 `versions.json`。

## 引擎缺口及修复

1. 旧视图按实体 ID 枚举容器内容，不能忠实表达牌堆顺序。改为读取席位投影的 `contents`，仍不读取隐藏真值。
2. 增加数据驱动的通用容器尺寸、纵向扇形叠放及仅影响表现的隐藏标记。目标区域高度随牌数增长；点击叠牌露出的部分可选中对应实体。Player 侧栏按选中对象列出规则提供的拖动去向。
3. 真人操作原先也受自动运行的步数保护限制。现在保护仅在 CPU 提交时执行；单人可持续搬牌和撤销。
4. JSON 反序列化丢失 SortedDictionary 的 Ordinal 比较器，`README.md` 等混合大小写路径使跨进程模块指纹发生变化。Wire 层为字符串有序字典统一序号比较和序列化顺序，未取消指纹检查。新增实际模块往返测试并完成回放、包、外部 Player 回归。
5. 复杂牌局中 Lua 动作枚举重复计算每张牌的连续序列和空列容量，触及沙箱指令预算。将这些计算复用于同一次查询，保持原沙箱预算和隔离策略。

## 自动验证

```powershell
dotnet build BoardGameBuilder.slnx -c Release
dotnet run --project tests/Engine.Tests -c Release --no-build
pwsh -NoProfile -File scripts/check-boundaries.ps1
```

上述构建、默认全量回归与依赖边界检查通过，构建 0 警告、0 错误。新增测试组为 `freecell` 和 `freecell-modes`。

另外以 `BGB_TEST_RELEASE=artifacts/freecell-player-release` 运行 `export` 测试组通过，覆盖正式导出预检、目标冲突、导出插件失败回滚及运行时篡改拒绝。

`freecell` 覆盖：

- 种子 1～20：52 张明牌、前四列 7 张／后四列 6 张、相同种子重跑一致、容器顺序与视图命中一致、移动与撤销、拒绝后状态不变、日志回放一致。
- 同色接牌、非法连续序列、非 A 开始收牌、错误花色、被占用空当、空当接收多张、未知实体、绕过规则直接 transfer 均拒绝。
- 连牌容量的空列和空当边界，特别是目标空列不计入容量；整组顺序保持与一步撤销；收牌撤销恢复计数。
- 目录和 `.bgm` 指纹与 52 张资源一致；Wire 往返指纹稳定；真人超过测试用自动步数上限继续，CPU 仍触发上限。
- 一个明确构造的 52 张胜利测试局面，以及真实随机种子 1 的独立通关路径。

`samples/freecell/solution-seed-1.json` 保存真实种子 1 的通关动作：洗牌、发牌、92 次搬牌，共 94 条命令。测试逐条确认动作由规则提供并通过权威提交，最终 `ended=true`、`winners=[p1]`、收齐 52 张，回放一致。该文件仅用于验收，不作为玩家提示或 CPU 的隐藏捷径。

`freecell-modes` 对同一通关路径逐条比较编辑器进程内调试会话、实际独立 Player 调试进程和普通 PlayerSession 的动作／拖动解析路径，帧、状态哈希和记录一致；记录可完整回放。外部模式使用实际 Player 的无图形调试入口，此项不冒充编辑器窗口的人工操作。

可复查产物位于 `artifacts/freecell-validation/`：`summary.json`、`seed-1.json` 至 `seed-20.json`、`seed-1-victory.json`、`modes-victory.json`、明确标为 fixture 的测试局面和日志。

## 图形与独立导出

使用 cua-driver 对实际 MonoGame Player 操作并截图核对：发牌后 8 列数量正确，将红桃 9 拖到空当 1 成功；再拖到梅花收牌区被拒绝，原牌位与行动数保持；点击撤销后返回原列，空当恢复为空。证据：`dealt.png`、`moved.png`、`illegal.png`、`undone.png`。这些交互截图在最后的透明边缘显示优化前采集；后续导出画面见 `export-playing.png`。

导出流程：

```powershell
pwsh -NoProfile -File scripts/publish-player.ps1 -Output artifacts/freecell-player-release
dotnet src/Player/bin/Release/net10.0-windows/Player.dll export artifacts/freecell-player-release samples/freecell artifacts/freecell-windows
```

实际自包含 `Play.exe` 加载导出的 `game.bgm`，通过图形宿主的 `--smoke <输出> --actions <动作文件>` 运行上述完整路径，自动重新开局并完成第二局。结果 `export-smoke.json` 为 `success=true, games=2`，两局最终视图一致，胜者 p1。这是图形宿主中的脚本化动作验证，并非 188 次鼠标输入；真实拖动另外验证如上。

188 次提交平均 40.29 ms，最大 142.30 ms（本机测量，含规则查询与 UI 更新，不含每步 120 ms 的展示等待）。运行中截图 `export-playing.png` 证明实际资源与视口正常渲染。导出不要求作者或玩家安装 SDK。

## 已知范围

- 只保存最后一步搬牌供撤销；不提供多步撤销、存档恢复或胜利后的撤销。不会自动收牌，避免改变玩家策略。
- CPU 是有限冒烟驱动：优先收牌，否则认输；没有宣称其能解出随机牌局。20 种子的回放通过不等于 20 局已通关。
- 本示例覆盖单人纸牌闭环，不能单独证明隐藏信息、多席位队友、六角战斗等能力；这些继续由既有示例和本轮全量回归验证。
- 未重新执行干净 Windows 环境实机／虚拟机验收；本轮证据是当前 Windows 上的自包含产物与实际图形运行。

## 1.1.0 快捷操作补充验收（2026-10-06）

对应 ROADMAP「补齐空当接龙的鼠标与键盘快捷操作」。交付更新为 `artifacts/freecell-windows-1.1.0/Play.exe`，包含更新后的 `玩法说明.md`；原 1.0.0 目录保留。模块指纹为 `847220371D8AF7C3004B58D189AD0CFE4F22C8363FB0A445E10A66A2FCE900EE`。

增加点牌再点目标、再次点击／Esc／空白取消、双击优先收牌否则进入首个空当、按住右键临时显示整张牌。中键及空白处右拖平移；键盘支持 1～8 选列／移列、0 空当、9 收牌、Ctrl+Z／F10／U 撤销、H 合法走法提示、Enter 洗牌／发牌、F2 新种子、F3 输入种子、Ctrl+R 当前种子重开。种子输入框内按 Enter 开始该种子的对局。完整操作表见 `samples/freecell/README.md`。

实现分工：Lua 为现有 ActionOffer 声明 Shortcuts，PlayerSession 将输入解析回同一权威命令。通用 View 状态机只处理选择、点击／拖动、按住看牌与取消；牌列数字通过表现属性 `viewHotkey` 声明。Editor 和 Player 共用视图、键位映射及动作解析。右键只能放大席位投影中已有的正面牌，不读取隐藏真值；仍只有一步撤销，不自动收牌。

验证结果：

- Release 构建 0 警告、0 错误；默认全量回归与依赖边界检查通过。最终打包前另行通过 `freecell freecell-shortcuts freecell-modes` 回归。新增 `freecell-shortcuts` 组覆盖点击与双击只提交一次、过期修订拒绝、拖动阈值、忙碌取消、失焦取消、按住看牌不提交命令、隐藏牌不可看、非法快捷动作及键位映射；完整种子 1 使用 60 次双击、32 次目标点击、2 次开始动作通关。
- 真实 MonoGame 窗口：种子 42，双击红桃 9 进入空当且只计 1 次行动；双击梅花 A 优先收牌；按住右键查看被遮住的红桃 A，松开恢复且行动数不变；点选空当红桃 9 再点黑色 10 所在列成功；F10 撤销恢复；按 1、9 选列并将方块 A 收牌；H 显示合法目标，Esc 取消高亮。截图位于 `artifacts/freecell-shortcuts/`，分别为 `double-cell.png`、`double-home.png`、`peek-held.png`、`peek-released.png`、`click-moved.png`、`f10-undo.png`、`numeric-home.png`、`hint.png`、`escape.png`。
- 正式自包含导出实际运行 `Play.exe gui <game.bgm> --smoke <输出> --actions <种子1动作>`。脚本经图形宿主的双击／点击输入解析路径连续两局获胜，结果 `artifacts/freecell-shortcuts/export-smoke.json` 为 `success=true, games=2`，两局最终视图相同、胜者 p1。188 次提交包括 120 次 double-click、64 次 click、4 次 action；平均 43.13 ms、最大 178.58 ms（不含每步展示等待）。这部分为脚本化交互意图，真实鼠标及键盘验证单列如上；`export-playing.png` 为导出运行画面。

以 `BGB_TEST_RELEASE=artifacts/freecell-player-release-1.1.0` 运行正式 `export` 回归亦通过，覆盖导出预检、目标冲突、失败回滚和运行时篡改拒绝。

本补充未重新验证干净 Windows 环境，也不把 H 的第一个合法走法当作解算器或最优策略。

## 1.2.0 经典桌面（2026-10-06）

对应 ROADMAP「经典空当接龙视觉与桌面布局」。新版交付 `artifacts/freecell-windows-1.2.0/Play.exe`，模块指纹 `88CA6C43AEBDDE4DECDDDF04C20454DB245C12A61AEF8CB646C42FA033B97CB0`，规则及 CPU 脚本散列与 1.1.0 一致。

- 桌面改为经典绿色，顶部四空当与四花色收牌位采用凹槽轮廓，中间为轻量王冠图案。去掉整列背景块和常驻动作侧栏；Kenney 原始牌面保持不变，增加轻阴影、金色选择／提示边框和真实牌组拖动预览。叠牌露出高度调整为 42，使更多花色可见。
- Player 改为灰色游戏／查看／帮助菜单与底部状态栏。直接打开模块即可进入桌面，按两次 Enter 或使用右下角按钮洗牌发牌；模块设置、动作面板和音效保留在菜单中。经典主题由 `viewTheme=1` 声明，牌位花色由 `viewSuit` 声明，不依据模块 ID 选择规则或读取权威状态。
- 经典桌面默认自动适应窗口，尺寸变化时居中并完整显示各列；手动缩放／平移后通过「查看 → 适应窗口」恢复。Editor 与 Player 仍使用同一 TableScene 和 InteractiveTable。
- Release 构建 0 警告、0 错误，默认全量回归和项目依赖边界检查通过。包括 20 种子发牌／回放、完整种子 1 快捷操作通关、Editor／独立 Player 会话一致性。未增加重复实现的视觉快照断言。
- 使用真实 MonoGame 窗口核对绿色桌面、牌位、八列、状态栏和双击移动；窗口从 1804×1367 调整到 1204×947 后仍完整显示牌桌。最终导出的小窗口画面为 `artifacts/freecell-classic/classic-table.png`；实际独立程序的通关中画面为 `export-playing.png`。
- 实际自包含 `Play.exe` 经图形宿主连续两局完整通关。`artifacts/freecell-classic/export-smoke.json` 为 `success=true, games=2`，两局最终视图一致；共 120 次 double-click、64 次 click、4 次 action 意图，平均 42.02 ms、最大 184.57 ms。这是脚本化输入意图验证，不冒充 188 次真实鼠标输入。

未重新验证干净 Windows 环境。新版与旧版使用不同导出目录，旧程序及其对局未被覆盖。
