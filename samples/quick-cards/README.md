# 快速卡牌对战

两席位各初始抽三张牌，轮流打出一张并累计牌面点数，牌库有牌时补一张。每人行动三次后比较总分，平分为共同胜者。CPU 选择自己手中点数最大的牌。

```powershell
dotnet run --project src/Player -- play samples/quick-cards --seed 42 --log artifacts/my-game.json
dotnet run --project src/Player -- replay samples/quick-cards artifacts/my-game.json
```

默认 p1 为真人、p2 为 CPU。先输入 1 开局，再按合法动作编号出牌；q 或输入结束可退出并保存当前日志。可用 `--cpu p1,p2` 运行全 CPU，或 `--human p2` 覆盖控制方式。

模块 JSON 的组件数组无序；牌序由容器 contents 明确指定。规则和 CPU 分别位于 rules.lua 与 cpu.lua，实体数据由 module.json 提供。命令行只显示当前席位投影；JSON 日志包含隐藏信息的权威状态，供本地作者诊断，不是可向玩家分发的日志。运行对局无需网络，首次构建需还原开发依赖。
