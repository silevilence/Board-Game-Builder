# 扫描资源索引

扫描资源共 **40 张 RGBA PNG**：33 张卡片正面（14 咒物、12 咒灵、3 角色、3 职业、1 帮助牌）、5 张共用卡背，以及调查日志正反两面。另已加入 7 个 Kenney 图标（各含 PNG／SVG）与 1 个开源中文字体文件。

- `cards/<类别>/fronts/` 为正面，`cards/<类别>/backs/shared.png` 为该类共用背面；咒灵正面再按 `level-1`、`level-2`、`level-3` 分组。
- 咒物文件编号严格沿用卡面 `001`～`014`；咒灵、角色、职业使用稳定英文文件 ID，中文名称见下表。英文 ID 不是官方译名。
- `journal/` 为调查日志两面，不是游戏牌背。骰子和方块使用下方 Kenney 素材，不从规则插图截取。
- 已将卡片／日志外侧扫描白底去成透明，保留印刷网点和内部原像素；帮助牌的白色纸面、牌内白字和白色骰面保持不透明。圆角边缘做抗锯齿及边缘颜色延展，避免透明图片贴到桌面出现白晕。无额外有损 JPEG 压缩。
- 全部卡边已检查；幻象 `phantasm.png` 左上角斜切缺口已补齐，幽灵 `wraith.png` 顶部增加 22 像素黑边以恢复同类牌的边框宽度，鬼魂及其余咒灵的外侧圆角黑边一并整理。补边只复制本牌现有黑边纹理，未生成或重绘插图／文字；原内容保留，幽灵仅整体下移 22 像素。
- 角色／职业正面是横向，卡背按图案直立保存，使用时应显式适配朝向，不能靠文件宽高推断牌种。幽灵补边后的高度与原裁剪不同，尺寸以清单为准。
- 背面关联依据扫描相邻页及图案分类，原 PDF 未展示逐张翻面对照；每类仅保存一张共用背面。
- [manifest.json](manifest.json) 记录来源 PDF 哈希、页码、原始像素四角、旋转角、输出尺寸、各图哈希和 `backAssetId`；`scanSha256` 保留去白底前版本的哈希，`pixelEditing` 记录后处理与补边。该清单是资料索引，不是尚未开发的引擎资源配置。

## 骰子、指示物与字体

- [Kenney Board Game Icons](icons/kenney-board-game-icons/README.md)：参考图蓝框中的 `dice_1`～`dice_6` 六个骰面，以及 `d6` 实心方形指示物。直接提取用户 ZIP 中 128 px PNG 与对应 SVG，附原包 CC0 许可证、路径映射与哈希。
- [Noto Sans CJK SC Regular](fonts/noto-sans-cjk-sc/README.md)：从官方仓库固定版本下载的完整静态 `.otf` 中文字体，附 OFL 1.1 许可证与来源／哈希；用于游戏动态文字，不依赖系统字体安装。

## 扫描图片

原资料：用户提供的《咒物调查员》扫描件；规则署名 ©2023 香蕉桌游，设计 Jason Greeno，插图／图标 Brooke Penrose、GameIcons.net。原 PDF 不随本目录重复保存。

| 类别 | 面 | 名称 | 文件 | PDF 页 |
| --- | --- | --- | --- | ---: |
| 调查日志 | 正面 | 调查日志：捕获目录 | [journal-captures](journal/front-capture-log.png) | 5 |
| 调查日志 | 背面 | 调查日志：灵异现象观察记录 | [journal-observations](journal/back-observation-log.png) | 6 |
| 帮助牌 | 正面 | 回合流程帮助牌 | [reference-turn](cards/reference/fronts/turn-sequence.png) | 5 |
| 帮助牌 | 背面 | 帮助牌共用背面：标题 | [reference-back](cards/reference/backs/shared.png) | 6 |
| 角色 | 正面 | 塞莱斯特 | [character-celeste](cards/characters/fronts/celeste.png) | 7 |
| 角色 | 正面 | 埃莉诺 | [character-eleanor](cards/characters/fronts/eleanor.png) | 7 |
| 角色 | 正面 | 利兰 | [character-leland](cards/characters/fronts/leland.png) | 7 |
| 角色 | 背面 | 角色牌共用背面：人物 | [character-back](cards/characters/backs/shared.png) | 8 |
| 职业 | 正面 | 不情愿的灵媒 | [profession-reluctant-medium](cards/professions/fronts/reluctant-medium.png) | 9 |
| 职业 | 正面 | 前侦探 | [profession-former-detective](cards/professions/fronts/former-detective.png) | 9 |
| 职业 | 正面 | 灵异调查员 | [profession-paranormal-investigator](cards/professions/fronts/paranormal-investigator.png) | 9 |
| 职业 | 背面 | 职业牌共用背面：双手 | [profession-back](cards/professions/backs/shared.png) | 10 |
| 咒物 | 正面 | 通灵板 | [object-001](cards/cursed-objects/fronts/001.png) | 11 |
| 咒物 | 正面 | 恐怖娃娃 | [object-002](cards/cursed-objects/fronts/002.png) | 14 |
| 咒物 | 正面 | 悲剧镜子 | [object-003](cards/cursed-objects/fronts/003.png) | 14 |
| 咒物 | 正面 | 针扎人偶 | [object-004](cards/cursed-objects/fronts/004.png) | 11 |
| 咒物 | 正面 | 芭蕾舞音乐盒 | [object-005](cards/cursed-objects/fronts/005.png) | 13 |
| 咒物 | 正面 | 诡异面具 | [object-006](cards/cursed-objects/fronts/006.png) | 12 |
| 咒物 | 正面 | 活肖像画 | [object-007](cards/cursed-objects/fronts/007.png) | 13 |
| 咒物 | 正面 | 禁书 | [object-008](cards/cursed-objects/fronts/008.png) | 12 |
| 咒物 | 正面 | 玩偶匣 | [object-009](cards/cursed-objects/fronts/009.png) | 11 |
| 咒物 | 正面 | 快乐猴子 | [object-010](cards/cursed-objects/fronts/010.png) | 14 |
| 咒物 | 正面 | 旧玩具屋 | [object-011](cards/cursed-objects/fronts/011.png) | 12 |
| 咒物 | 正面 | 诅咒录像 | [object-012](cards/cursed-objects/fronts/012.png) | 15 |
| 咒物 | 正面 | 古怪护符 | [object-013](cards/cursed-objects/fronts/013.png) | 13 |
| 咒物 | 正面 | 命运机器 | [object-014](cards/cursed-objects/fronts/014.png) | 14 |
| 咒物 | 背面 | 咒物牌共用背面：圣杯 | [object-back](cards/cursed-objects/backs/shared.png) | 15 |
| 咒灵 | 正面 | 吵闹鬼 | [spirit-poltergeist](cards/spirits/fronts/level-1/poltergeist.png) | 16 |
| 咒灵 | 正面 | 还魂者 | [spirit-revenant](cards/spirits/fronts/level-1/revenant.png) | 16 |
| 咒灵 | 正面 | 幻象 | [spirit-phantasm](cards/spirits/fronts/level-1/phantasm.png) | 16 |
| 咒灵 | 正面 | 幻影 | [spirit-apparition](cards/spirits/fronts/level-1/apparition.png) | 16 |
| 咒灵 | 正面 | 幽灵 | [spirit-wraith](cards/spirits/fronts/level-2/wraith.png) | 17 |
| 咒灵 | 正面 | 报丧女妖 | [spirit-banshee](cards/spirits/fronts/level-2/banshee.png) | 17 |
| 咒灵 | 正面 | 幽魂 | [spirit-specter](cards/spirits/fronts/level-2/specter.png) | 17 |
| 咒灵 | 正面 | 溺鬼 | [spirit-drowned](cards/spirits/fronts/level-2/drowned.png) | 17 |
| 咒灵 | 正面 | 鬼魂 | [spirit-ghost](cards/spirits/fronts/level-3/ghost.png) | 18 |
| 咒灵 | 正面 | 阴魂 | [spirit-shade](cards/spirits/fronts/level-3/shade.png) | 18 |
| 咒灵 | 正面 | 食尸鬼 | [spirit-ghoul](cards/spirits/fronts/level-3/ghoul.png) | 18 |
| 咒灵 | 正面 | 恶魂 | [spirit-evil-spirit](cards/spirits/fronts/level-3/evil-spirit.png) | 18 |
| 咒灵 | 背面 | 咒灵牌共用背面：骷髅 | [spirit-back](cards/spirits/backs/shared.png) | 19 |

## 运行派生资源

`runtime/manifest.json` 关联每张派生图与保留原图的 SHA-256。卡片高度至多 1024，日志 1600，预乘透明度后以 Lanczos 缩放，避免白色边缘；原始裁剪始终留在上表路径。

`table/tabletop.png` 是确定性生成的深色桌布；`table/ability-01.png` 至 `ability-27.png` 是扫描中已揭露能力文字的统一裁片，矩形位置和来源哈希见 `table/manifest.json`。同一能力只使用一种图像，隐藏咒灵完整牌面直到规则允许揭露。运行包只收集显式注册的派生资源、Kenney 图标、字体及许可证，不重复携带原图。
