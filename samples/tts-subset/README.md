# TTS 首版约定样例

此合成存档用于 M3 的确定性转换验收，不是特定商业游戏。
导入包含普通卡、自定义牌堆、普通牌堆、本地卡图、桌布与 Transform 位置；
远程卡图和复杂模型必须出现在报告中，来源脚本必须原样留档且不执行。
cards.png 是项目生成的橙／蓝色 2×2 卡图。

首版按 [TTS 官方存档格式](https://kb.tabletopsimulator.com/custom-content/save-file-format/)
解释 CardID：整数百位选 CustomDeck，余数从零索引卡图格。
Transform.posX／posZ 乘 100 映射到二维位置；高度、旋转、缩放明确报告为未转换。
普通内置卡无外部图片时保留文字牌面。支持存档目录内的本地图片；不联网下载资源。
