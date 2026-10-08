# 扑克牌 UNO 展示运行说明

源目录 `samples/poker-uno`，规则见 [示例说明](../samples/poker-uno/README.md)，验收见 [验证记录](validation/poker-uno-review.md)。

## 本地游玩

开发运行 `dotnet run --project src/Player -- gui samples/poker-uno`；发行目录双击 `Play.exe`。F3 选择 4／5／6 人，勾选真人，其余为 CPU。支持单人、热座、同屏分视角及全 CPU 公共观战；后者仅展示公共牌和手牌数量。

开局依次执行洗牌发牌、翻开首牌。点牌再点中央弃牌区或拖动出牌；动作栏提供摸牌、回收弃牌和结束回合。无牌可出才摸一张，只有刚摸的那张可当回合打出，也可结束回合。先出完获胜；无牌可摸且整轮无人出牌为平局。大量手牌可通过动作列表选择，按住右键查看完整牌面；缩放后可使用“适应窗口”。

## 联机与观战

Server、Player 和游戏包使用同一版本。客户端入口为“文件 → 联机 / 公共观战”。跨机要求 HTTPS，本机回环测试可用 HTTP。详细部署见 [联机说明](networking.md)。

将以下配置保存为仓库根目录的 `uno-server.json`，替换示例密钥；路径均相对于配置文件：

```json
{
  "module": "samples/poker-uno",
  "preset": "6 人",
  "listen": "http://127.0.0.1:5080/",
  "roomKey": "replace-this-room-key",
  "controllers": {"p1":"cpu","p2":"human","p3":"cpu","p4":"human","p5":"cpu","p6":"cpu"},
  "seed": 1,
  "output": "artifacts/uno-server-records"
}
```

开发运行 `dotnet run --project src/Server -- uno-server.json`，发行运行 `Server.exe uno-server.json`。所有真人席位加入后开局，任意位置可安排 CPU；全 CPU 服务器将全部 controller 设为 cpu。

两个 Player 加载相同游戏，填写服务器地址、密钥和相同人数预设，点击“读取房间与可用席位”，分别选择 p2、p4 加入。第三个窗口选择公共观战。不同预设的指纹不同，加入会被拒绝。NetworkClient 配置也支持 `preset` 字段。

关闭窗口或“暂时断线”保留恢复资格；“恢复连接”追赶权威状态。凭据保存在当前用户的 `%LOCALAPPDATA%/BoardGameBuilder/connections`，不要共享。服务器重开后选择“使用新身份”清理本机旧凭据；此操作不会移除仍存活房间中的旧占用。主动离开玩家席位会结束会话，观众离开不影响对局。CPU 接管须由服务器显式配置，默认等待重连。

联机窗口支持查看、保存本人获准的投影记录和滑块回放，不执行规则／CPU，不公开他人历史手牌。协议现为 2，增加实际控制方式元数据，旧客户端加入时明确拒绝；记录格式仍为 1，当前读取器兼容协议 1 的历史投影记录。

## 作者、图集与验证

编辑器打开 `game.start.json`。`definition.module.configuration.path` 选择默认人数，`presets` 声明三份定义。当前 path 使用结构化作者界面，其他定义可通过通用文件编辑修改。切换默认人数可撤销／重做，打包收集全部预设及资源。

`assets/cards.json` 使用 TexturePacker JSON Hash，描述唯一图片 `assets/cards.png` 中的 52 张正面及一张牌背。定义仅声明 `"cards": "assets/cards.json"`，牌面直接写 `"card_hearts_A"`、`"card_back"` 等帧名。加载和打包自动包含 JSON 引用的 PNG；绘制共享纹理，打印按帧还原，不生成单牌 PNG。FreeCell 没有牌背，牌背来自同一 Kenney 包在 island-settlers 中的资源；许可在 assets/License.txt。`pwsh -File scripts/build-poker-uno.ps1` 重建图集、JSON Hash 及三份定义，需要 Windows System.Drawing 和 Python 3，无 Pillow 依赖。

```powershell
dotnet build BoardGameBuilder.slnx -c Release
dotnet tests/Engine.Tests/bin/Release/net10.0/Engine.Tests.dll poker-uno poker-uno-modes poker-uno-online poker-uno-batch
```

真实图形验证：`Player.exe --verify-showcase samples/poker-uno <结果.json>`，输出结果与视口 PNG，检查 53 个区域只使用一张纹理。编辑器验证：`Editor.exe --verify-session samples/poker-uno <结果.json>`，覆盖两种试玩、脚本重载、单步、身份观察与回放。

发布引擎：`pwsh -File scripts/publish-player.ps1 -Output <新发行目录>`；导出游戏：`Player.exe export <发行目录> samples/poker-uno <新游戏目录>`。全部人数预设进入包，玩家无需 SDK。服务器独立发布使用 `scripts/publish-network.ps1`；Player 发行物本身不承载服务器。
