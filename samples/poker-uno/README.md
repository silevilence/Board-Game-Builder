# 同花同点 · 扑克牌 UNO

一副 52 张扑克牌，4～6 人，每人 7 张。与弃牌堆顶同花色或同点数即可出一张；A/J/Q/K 是普通牌，无功能牌、万能牌或喊 UNO 惩罚。无牌可出时摸一张，仅此张能立即打出，也可结束回合；不能出则结束回合。先清空手牌获胜。

开局由当前席位依次执行洗牌发牌、翻开首牌。牌堆耗尽时保留顶牌，其余弃牌洗回摸牌堆。无牌可摸且连续整轮无人出牌时平局；自动验证步数上限仅为故障保护，不属于获胜规则。

开发运行：`dotnet run --project src/Player -- gui samples/poker-uno`。F3 选择 4／5／6 人及单人、热座、分视角或全 CPU。未选为真人的席位均由 CPU 控制。图形联机入口在文件菜单；服务端与客户端必须使用相同的人数预设及游戏版本。

全部牌面来自 FreeCell 的 Kenney Playing Cards Pack；FreeCell 无牌背，牌背复用同一资源包在 island-settlers 中的 card-back.png。许可见 assets/License.txt（CC0）。`assets/cards.json` 使用 TexturePacker JSON Hash，引用唯一图片 `cards.png`；面资源直接使用帧名，如 `card_hearts_A`、`card_back`。`pwsh -File scripts/build-poker-uno.ps1` 可重建图集、JSON Hash 和三个人数定义，需要 Windows System.Drawing 和 Python 3，无 Pillow 依赖。图集保留 2 像素透明边距。

运行入口为 game.start.json，通用定义预设为 `4 人`、`5 人`、`6 人`；规则与 CPU 均由 Lua 承载。`resources.texturepacker` 配置从 `assets` 读取 `*.json`，自动纳入配套 PNG；基础 `resources` 将 `player.json` 注册为 `player.presentation`，定义只引用资源名。目录／通配符规则见 [运行插件协议](../../docs/runtime-plugins.md)。本地多人同屏存在物理旁观，热座交接时先遮蔽上一位玩家手牌。权威联机只发送本席位投影；图集包含全套牌面不代表客户端知道隐藏牌的归属或顺序。
