# 流程卡牌原型

`module.json` 是从 quick-cards 复制的独立示例配置，使用相同的通用实体、容器与基础布局能力；两者不自动同步，后续各自维护。示例规则全部在本目录。
默认一名真人和一名 CPU。开始自动洗牌、发牌；玩家出牌后经 judge 判定继续或结算。
同分走 draw 结束节点，唯一胜者走 win。CPU 使用合法动作，不读取系统状态。

`bgb call project.validate --project samples/flow-cards` 校验。
`bgb call run.cpu --project samples/flow-cards --args '{"count":5,"limit":30}'` 批量验证。
`Player.exe play samples/flow-cards` 可按编号进行真人加 CPU 对局；gui 路径可直接试玩。
可在 flow.json 修改连接或引用独立短脚本，修改后重建会话。
