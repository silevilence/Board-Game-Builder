# 扫描资源索引

共 **40 张 PNG**：33 张卡片正面（14 咒物、12 咒灵、3 角色、3 职业、1 帮助牌）、5 张共用卡背，以及调查日志正反两面。

- `cards/<类别>/fronts/` 为正面，`cards/<类别>/backs/shared.png` 为该类共用背面；咒灵正面再按 `level-1`、`level-2`、`level-3` 分组。
- 咒物文件编号严格沿用卡面 `001`～`014`；咒灵、角色、职业使用稳定英文文件 ID，中文名称见下表。英文 ID 不是官方译名。
- `journal/` 为调查日志两面，不是游戏牌背。实体骰子和方块未单独扫描，不以规则插图冒充其独立资源。
- 图像保留原扫描分辨率附近的尺寸、印刷网点和圆角外的少量白底；已裁正阅读朝向，无额外有损 JPEG 压缩。角色／职业正面是横向，卡背按图案直立保存，使用时应显式适配朝向，不能靠文件宽高推断牌种。
- PDF 第 16 页幻象、第 17 页幽灵、第 18 页鬼魂贴到原页面右边缘，边缘可能有轻微截断；原图如此，未补绘。其文字和挑战可读。
- 背面关联依据扫描相邻页及图案分类，原 PDF 未展示逐张翻面对照；每类仅保存一张共用背面。
- [manifest.json](manifest.json) 记录来源 PDF 哈希、页码、原始像素四角、旋转角、输出尺寸、各图哈希和 `backAssetId`。该清单是资料索引，不是尚未开发的引擎资源配置。

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
