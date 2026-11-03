# 灯塔补给 · 纯配置节点

在营地带上一件装备，出发查看天气，再选择海路、山路或返回。所有规则均在 `flow.json`，不启用 Lua、WASM 或 CPU 插件。

```powershell
dotnet run --project src/Player -- gui samples/flow-supply
dotnet run --project src/Player -- play samples/flow-supply
dotnet run --project tests/Engine.Tests -c Release -- flow-supply
```

编辑器打开 `game.start.json`，在流程画布查看、修改节点；CLI 可执行 `bgb validate --project samples/flow-supply`、`bgb call flow.read --project samples/flow-supply`。保留 p1 为真人；自动验收由测试程序选择同一组合法动作，不使用 CPU 批量入口。

## 玩法

- 营地可反复选择绳索、提灯或不带装备；一次只带一件。确认出发后掷一次六面骰：1～3 风雨，4～6 晴朗。
- 山路配绳索，海路配提灯。晴朗或装备匹配即可送达；两者同时满足获得嘉奖。
- 风雨中选择不匹配的路线会失败；也可主动返回营地结束本局。
- 重新开局可试其他选择或种子。恢复与回放使用记录结果，不重新掷骰。

## 配置对应关系

| 节点／连线 | 演示能力 |
| --- | --- |
| `start` | 起点以标准命令设置提示 |
| `pack` | `seat.fixed`；静态动作设置装备，`complete:false` 停留并允许反复修改 |
| `weather` | 系统节点用 `roll` 产生受控随机，无脚本 |
| `route` | `seat.current`；动作以 `coast`／`ridge`／`home` 命名结果离开节点 |
| `judge-coast`／`judge-ridge` | JSON Pointer 读取属性，`ge`、`eq`、`all`、`any` 表达条件 |
| `*-bonus`／`*-safe` | 同时匹配时，优先级 20 的嘉奖出口胜过优先级 10 的普通送达出口 |
| `*-blocked` | 无匹配时进入 `default:true` 出口 |
| 四个 end 节点 | 设置结局提示，以 `["p1"]` 或 `[]` 结算 |

`route` 也声明 `done` 和返回营地的对应边，以满足节点进入时的默认结果契约；实际三个玩家动作使用各自命名结果。静态动作没有动态显隐条件，装备替换使用幂等 `set`，避免重复累加。

`module.json` 定义席位、装备、天气骰和状态面板；`player.json` 提供标题／帮助／提示，`board.json` 提供静态说明。作者配置独立于运行入口；可使用正式打包、Player 导出与录制回放。

## 验证

`flow-supply` 测试找到覆盖六种骰面的种子，执行 **6 天气 × 3 装备 × 3 路线 = 54 条路径**，分别核对目录与包、四个结局、优先级／默认出口、停留动作、过期动作拒绝、等待节点恢复与回放。

```powershell
dotnet run --project tests/Player.Hosting.Tests -c Release -- --samples .scratch/flow-supply-gui/report.json flow-supply
```

图形验证更换装备并经海路送达，检查目录／包、窗口缩放和释放。`python scripts/build-grid-flow-samples.py` 可重建两个教学示例的 JSON，会覆盖已有配置。见 [验收记录](../../docs/validation/teaching-samples.md)。
