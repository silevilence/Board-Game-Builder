# 游戏流程插件

流程由 `game.start.json` 的 `flow` 插件（version `1`、api `1`）配置 `path` 指定。
它独占 `rules` 能力，与 `lua.rules`、`rules.commands` 互斥；未启用的旧工程保持原行为。
文件是 `{ "format": 1, "nodes": [...], "edges": [...] }`，节点和连线使用稳定 `id`。
共享契约是 Runtime 的 `FlowDefinition`／`FlowFile`，不依赖 Editor。

## 节点与执行

- `start`：必须唯一，无入边；执行 commands/script 后离开。
- `system`：以系统身份读取权威状态并执行 commands/script。
- `player`：进入时执行 commands/script，然后解析 seat 并等待。seat 只选择一种：
  `{"fixed":"p1"}`、`{"current":true}` 或 `{"script":{"path":"seat.lua","entry":"select"}}`。
  玩家和 CPU 使用同一合法动作及提交入口；行动席位与当前回合席位独立。
- `end`：至少一个，无出边；执行 commands/script 后用 winners 确认终局，空数组表示平局。

每次节点进入或玩家提交为一个事务：读取输入 → 执行配置命令 → 脚本 → 应用脚本命令 →
在最新候选状态上选择连线 → 提交流程位置和效果。失败撤销整个事务。
玩家进入后的重复动作不会重跑进入脚本；`complete:false` 保持等待，`complete:true` 离开。
每次真正进入节点清空 nodeContext，gameContext 保持到对局结束。两者均为权威私有 JSON 对象，
合计最多 16 KiB，不能放入 CPU／玩家投影或普通错误消息。parameters 是该节点只读配置。
节点不是 phase 或 turn；`next`／`phase` 只改变游戏回合／阶段，不改变流程位置。
`finish` 仅由结束节点内部提交；脚本不能返回目标节点 ID。

## 脚本与动作

脚本引用为 `{"path":"rules.lua","entry":"execute"}`，文件返回包含函数的 Lua 表。
执行函数返回 `{commands={}, result="done", complete=true, gameContext=..., nodeContext=..., winners={...}}`。
上下文字段省略时保留原值，指定时整体替换。结果必须在节点 results 列表中，默认 `["done"]`。
系统输入包含 state、parameters、gameContext、nodeContext；玩家动作查询／裁决输入只含 view、parameters，
裁决另带 command。玩家脚本不能读取或修改私有上下文；后续系统节点可根据更新后的状态判定。
动态席位函数返回席位 ID。所有脚本使用现有隔离沙箱，状态变化只通过标准 commands；
随机效果使用内核 shuffle／roll，条件和查询不能消耗权威随机状态。

玩家节点可配置 actions：`{id,label,commands,complete,result}`；也可成对提供 actionsScript 和
decideScript，分别返回标准 ActionOffer 数组和上述裁决结果。合法动作先查询再精确匹配提交，
提交还绑定流程修订号，过期动作被拒绝。动态动作的命令不得直接控制流程图。

## 连线与条件

连线包含 id、from、to、result、condition、priority、default。
只考虑与命名结果相同的出口，priority 越大越优先，同最高优先级多个匹配报错；
无匹配时使用唯一默认出口，再无出口则报错。默认出口不得再带条件。
条件省略表示 true；op 支持 true、false、eq、ne、lt、le、gt、ge、all、any、not、lua。
比较的 left/right 各取一种 `{"literal":...}` 或 `{"path":"/state/turn"}`（JSON Pointer）；
逻辑组合使用 items，lua 使用 script 引用。比较只支持相同类型的标量，大小比较只支持数字。
条件只读，在候选状态上执行，不能产生效果或改写上下文。

## 文件边界

未知版本、未知字段、缺必需字段明确拒绝；编辑器可保留无效草稿并展示共享诊断。
首期平面图，不含嵌套、并行、实时计时器或同时等待多个席位。
流程及脚本必须纳入运行身份与打包；变更后显式重建会话，不热换正在执行的图。

## 结构校验

`FlowValidation.Validate` 返回关联节点／边的诊断；`RequireValid` 阻止无效图运行。
不可达节点是 warning；缺端点、结果引用错误、非法起止边及无潜在终局路径为 error。
先排除恒假边，再从全部结束节点逆向求可达集，因此封闭循环和流入封闭循环的节点均被拒绝。
可正常退出的回合循环、玩家继续等待和未知动态条件不会被误判为死循环。
常量折叠只处理标量比较及 all/any/not。Lua 只识别整文件 `return false`，或整文件唯一的
`return { entry = function(input) return false end }`（entry 与引用一致，允许空白和分号）；
带其他语句、注释或外部变量的脚本一律视为未知，不运行 Lua 来猜测能否终止。
已知恒真分支会屏蔽同结果的默认出口和较低优先级出口，这些被屏蔽的边不作为有效终局路径。
all/any 从左到右短路求值；比较支持字符串、布尔值和 decimal 范围内的数字，大小比较只支持数字。
图文件上限 1 MiB，单脚本 256 KiB；节点／结果／连线 ID 最长 128 个字符，内容总量沿用模块限制。
玩家查询和裁决的伪随机序列仅取决于可见投影，不传入权威随机状态；持久随机效果仍通过内核命令记录。

## 自动推进与恢复操作

插件配置 `maxAutomaticSteps` 默认 256，范围 1..100000。每次进入节点消耗一步；到达玩家等待或结束
立即停止推进。超限产生 `flow-budget`，保留已提交状态及最多 64 条权威轨迹，不称为死循环。
全 CPU 的动作总数另由 PlaySession 的 maxSteps 限定，耗尽后须重开。
`CancelFlow()` 可从其他线程取消当前推进／Lua 请求；`ResumeFlow(token)` 仅允许继续预算或取消暂停，
从当前已提交节点继续，不重放效果。结构错误、脚本错误、无动作或分支错误须修正内容后重开。
构造 PlaySession 时可传 cancellation 作为会话流程的生命周期取消令牌；该令牌取消后需重开。
事务失败记录使用固定诊断代码，脚本抛出的文本不会进入玩家错误或记录；私有上下文和轨迹只供权威作者查看。

## 记录与宿主

`ExportRecording()` 保存运行身份、流程／脚本原字节和实际效果。系统记录 `execution: "system"`、空 actor；
玩家记录 `execution: "player"` 和真实席位。旧模式省略 execution 与 flow 字段，保留旧记录校验语义。
每次事务末尾的 flow-state 效果保存完整续行信息；回放只应用记录，不执行 Lua。
`PlaySession.Restore(module, recording, controllers)` 校验内容身份后恢复。等待节点直接继续等待，
终局不再执行；系统边界以 `flow-restored` 暂停，须显式 `ResumeFlow()` 才执行下一个尚未提交的节点。
正在执行的事务没有中间快照，恢复时使用最后已提交边界。改变图、脚本或版本须重建。
恢复保留动作计数和完整历史，不能通过恢复绕过 CPU 动作上限。
Editor 试玩的共享 DebugSession、PlayerSession 与既有 OnlineSession 均使用同一 PlaySession；
联网重连只刷新投影，不恢复或重入节点。普通投影只有当前行动席位，不包含私有游标上下文。
