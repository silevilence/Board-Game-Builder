# 方格、原型与纯配置流程示例验收

2026-11-03，本机 Windows x64 / .NET SDK 10.0.112。交付 `samples/polyomino` 和 `samples/flow-supply`，不修改引擎、Player 或 Editor 的生产代码。

## 规则与作者配置

```powershell
dotnet run --project tests/Engine.Tests -c Release -- layouts prototypes flow-contract flow-validation
dotnet run --project tests/Engine.Tests -c Release -- polyomino flow-supply application-migration
dotnet run --project src/Cli -c Release -- validate --project samples/polyomino
dotnet run --project src/Cli -c Release -- validate --project samples/flow-supply
dotnet run --project src/Cli -c Release -- call flow.validate --project samples/flow-supply
```

以上通过。流程校验只有状态条件的 `conditional-exit` 信息提示，无错误。

- Polyomino：16 格的共享原型、字段继承／覆盖移除与 Undo；每块作为原子命令组移动；四次旋转还原；方格拖拽；越界、重叠、非手柄拆块、小数坐标及错误选择拒绝后状态不变；收回再放置；完整铺满与放弃。种子 1～5 的 CPU 演示全部获胜，目录／包、录制回放及席位投影一致。
- 灯塔补给：没有 Lua／WASM 插件或节点脚本。六种天气、三种装备、三条路线共 54 个组合，各跑目录与包；四种结局、优先级、默认出口、装备更换停留、过期动作拒绝、等待节点恢复和回放均正确。
- 两个入口显式声明 `board.render`；新增为新格式作者样例，不伪造历史迁移前状态。既有迁移清单检查及原有目录／包／作者依赖验证通过。

## 真实 Player

```powershell
dotnet run --project tests/Player.Hosting.Tests -c Release -- --samples .scratch/teaching-samples/gui-verified/report.json polyomino flow-supply
```

目录和 `.bgm` 各执行一次，共四次完整对局。Polyomino 通过实际动作按钮、方格手柄拖拽、收回再放置完成获胜；灯塔补给依次更换绳索／提灯、确认出发并经海路送达。两例都调整窗口到 900×680 并继续绘制；关闭后设备、后端及原生窗口计数均为零。

原始结果：[宿主报告](evidence/teaching-samples-player.json)、[四次对局报告](evidence/teaching-samples-games.json)。本地截图位于 `.scratch/teaching-samples/gui-verified/`，包含 initial、won、resized 三种状态；已目检拼板开局／终局及灯塔结果页。初轮发现待放区旋转遮挡、网格穿过说明文字，已扩大待放间距、将说明放入普通面板、将骨牌标记写入几何图片后复验。

## 范围

本轮检查两个示例、关联契约与示例清单；没有重跑全仓测试、全部既有游戏的图形验收、独立发行或干净 Windows 验收。原型作者操作通过实际 ModuleDocument 草稿／Undo 与运行加载器验证，未另做编辑器人工点击验收。

Polyomino CPU 使用样例公开配置中的固定解，不是通用解算器；拖拽预览只移动手柄格，提交后整块更新。灯塔补给是单真人无脚本流程，不提供 CPU 策略。生成脚本只依赖 Python 标准库，输出固定 LF 的 JSON 与原创几何 PNG；重建会覆盖两个样例的配置，README 已注明。
