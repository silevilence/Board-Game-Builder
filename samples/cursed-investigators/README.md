# 咒物调查员

发布版 CLI 与状态机引擎的可玩示例：**标准单人、两局战役、初学者变体**。关卡和挑战者保留扫描资料，本轮未实现。

## 开始游戏

从仓库根目录运行本次完整发行版：

```powershell
artifacts/cursed-investigators/title-v11/player/Player.exe gui samples/cursed-investigators
```

独立导出目录双击 `Play.exe`，无需源码、PDF、下载目录、编辑器或 .NET SDK。导出方法见 [CLI 使用说明](CLI-USAGE.md)。

打开后依次进入 **标题 → 模式 → 人物 → 职业 → 开始游戏**。模式可选标准单人／两局战役，并可启用初学者；人物和职业显示牌面预览，可返回上一步修改，默认利兰＋灵异调查员。点击“开始游戏”才洗牌发牌。对局结束后可先翻阅日志，再点击“返回标题”开始新调查，上局状态不会带入。

右侧操作栏默认隐藏，右侧只保留调查日志；**下方操作区**列出当前合法的重掷、改点、职业能力、挑战和奖励，可滚动查看。调查目录从下往上；双击咒物或点动作按钮选择。双击骰子切换选择。未选骰子时，挑战自动选择一组满足要求的骰子；选中骰子时只使用所选范围。每回合最多一次挑战，奖励立即选择。需要旧动作栏时可在“查看”菜单切换。

桌面滚轮或「放大／缩小」可查看扫描文字，中键拖动平移，「查看 → 适应窗口」复位。左侧为角色、职业及封存物件，中间为分层遭遇，右侧为当前咒物／咒灵与已揭露能力，下方为骰池；职业格中的小骰子表示保留。白色方块标记属性和已完成挑战。发牌期间输入短暂锁定，动画完成后继续。

侧栏日志可翻页，捕获、奖励使用、观察次数和声望来自已提交状态；「简要说明」另开参考页，关闭即可返回，不消耗行动。下方文字提示随阶段更新。游戏菜单支持新开、重开和开局设置；CPU 可自动对局，但策略不保证胜利。

## 规则、数据和资源

- [规则转写](RULES.md)、[全部卡牌](CARDS.md)、[sample 裁定](RULINGS.md)：扫描缺失边界已获授权裁定，明确区别于官方规则。
- [资源索引](assets/README.md)、[原图清单](assets/manifest.json)：33 张正面、5 张共用背面、日志两面，保留透明化和修边后的原图及来源。
- `assets/runtime`：等比例派生纹理；`assets/table`：桌面和从扫描提取的能力条。同能力共用图片，避免揭露能力时提前显示咒灵身份。
- `assets/icons`：Kenney 六个骰面和方块；`assets/fonts`：Noto Sans CJK SC 及许可证。动态文字直接使用包内字体字节。
- `game.start.json` 注册通用插件；`position.json` 定义实体；`flow.json` 表达选择、设置、修正、骰池、奖励、回合结束与终局；`game.lua` 承载全部游戏规则及 CPU。
- `animations.json` 描述洗牌／发牌；`documents.json` 定义日志和参考页；`presentation.json` 定义标题步骤、窗口、动作区和终局返回入口。标题导航不改变规则，选项通过现有状态机合法动作提交。

资源来源：用户提供的 19 页扫描件，规则标注 ©2023 香蕉桌游，设计 Jason Greeno，插图／图标 Brooke Penrose、GameIcons.net。原 PDF 不进入仓库。扫描素材不因 Kenney 或 Noto 的开放许可证而获得相同授权；后两者许可证保存在各自目录。

## 维护与验证

`tools/game-logic.lua` 与 `tools/build-project.py` 是作者源；生成 JSON 编辑请求后，用发布 CLI `files.write` 应用，避免只改生成文件后被覆盖。`prepare-textures.py` 校验原图哈希并按预乘透明度缩放；`prepare-table.py` 生成桌布和能力条，都不修改原扫描。需要 Python 和 Pillow。

```powershell
dotnet run --project tests/CursedInvestigators.Tests -c Release
dotnet run --project tests/CursedInvestigators.Tests -c Release -- play 1 beginner
dotnet run --project tests/CursedInvestigators.ViewTests -c Release
```

规则测试覆盖全部挑战组合、27 种能力、14 件咒物的关键效果、属性轨道、职业、奖励与战役边界。自动整局上限 2400 次玩家操作，图形验证需要 Windows / Direct3D 11。发布 CLI、独立 Player 及证据见 [验收记录](../../docs/validation/cursed-investigators.md)。
