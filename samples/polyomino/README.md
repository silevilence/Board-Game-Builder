# Polyomino · 方格拼板

4×4 单人拼板，展示方格坐标／吸附与原型／实例字段继承。一块 I、一块 O 和两块相同的 L 形骨牌（A、B），共 16 格；不重叠地铺满棋盘即获胜。

```powershell
dotnet run --project src/Player -- gui samples/polyomino
dotnet run --project src/Player -- play samples/polyomino
dotnet run --project tests/Engine.Tests -c Release -- polyomino
```

编辑器打开本目录的 `game.start.json` 即可编辑和试玩；CLI 可执行 `bgb validate --project samples/polyomino`。作者插件配置在 `author.profile.json`，不进入运行包。

## 操作

1. 在右侧动作栏选择 I、O、A 或 B。初始选择 I，状态栏显示当前编号。
2. 顺时针旋转到需要的方向，再选择合法落点。落点是形状外接框左上角，列／行均从 0 开始。
3. 也可拖动当前骨牌标有 `*` 的单元格。像素拖拽吸附到方格坐标，规则移动整块。`*` 不一定是外接框左上角，拖动以它本身的位置为准。
4. 已放置的骨牌可移位、合法旋转或收回待放区；无法旋转时先收回。全部放好即结算，也可结束未完成的本局。通过 Player 游戏菜单重新开局。

拖动越界、重叠、非当前骨牌或非手柄单元格会被拒绝，状态不变。拖拽预览沿用通用视图，只显示所拖的单元格，松手提交后整块同时移动。

一组解（从初始方向顺时针计数）：

| 骨牌 | 旋转次数 | 外接框落点 |
| --- | --- | --- |
| I | 1 | (0,0) |
| O | 0 | (1,0) |
| A | 0 | (2,0) |
| B | 1 | (1,2) |

```text
I O O A
I O O A
I B A A
I B B B
```

F3 将 p1 设为 CPU 可观看演示；也可运行 `Player batch samples/polyomino --seed 1 --count 5 --out <新目录>`。CPU 只读公开投影与合法动作，按手柄的 `goalX/goalY/goalR` 执行上述解，先收回不符合示范解的摆放。它不是通用解算器。本例没有随机设置，不同种子下相同操作产生相同摆放。

## 方格与原型

`module.json` 的 `layout.kind="square"`、`size=64` 将逻辑列／行映射到屏幕。每块骨牌由四个普通实体组成，`piece` 分组，`cell` 排序，`dx/dy` 描述初始形状。旋转、合法性和胜利属于 sample Lua，宿主没有 Polyomino 专用逻辑。

16 个单元格都引用原型 `cell`，继承归属、可见性、尺寸、图层与默认牌面；实例覆盖位置、形状数据、牌面名称和图片。实例 `faces` 不重复 `up`，它仍从原型继承；数组字段整体替换。

在工程副本中观察继承：

1. 在“原型与实例”面板或 JSON 中，将 `cell` 的 `properties.viewWidth` 从 58 改为 50。重新试玩后所有格变窄，规则不变。
2. 将原型 `properties.material` 从 1 改为 3。I、O、A 继承 3；B 的四个实例显式覆盖为 2，仍保持 2。这个字段只作教学，不参与规则。
3. 从 `B-0` 的实例 `properties` 删除 `material`，它恢复继承 3，B 的其他格仍为 2。Undo 可恢复编辑；运行中的对局须重开才使用新定义。

`python scripts/build-grid-flow-samples.py` 重建两个教学示例的初始 JSON 和几何 PNG，会覆盖 JSON，不覆盖 Lua 或 README。图片由脚本生成，无外部素材。修改形状须同时检查总面积、待放区及示范解。

## 验证

`polyomino` 测试覆盖继承／覆盖移除／Undo、四次旋转还原、拖拽、越界／重叠／拆块／小数坐标拒绝、收回、完整获胜、放弃、5 种子 CPU、目录／包及回放。

```powershell
dotnet run --project tests/Player.Hosting.Tests -c Release -- --samples .scratch/polyomino-gui/report.json polyomino
```

图形验证通过实际按钮与手柄拖拽完成整局，检查目录／包、窗口缩放及资源释放。见 [验收记录](../../docs/validation/teaching-samples.md)。
