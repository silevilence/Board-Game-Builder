# 状态机流程与节点脚本

运行入口添加 `{"id":"flow","version":"1","api":1,"configuration":{"path":"flow.json"}}`，
移除 `lua.rules`／`rules.commands`，保留 definition、kernel、session.local、recording；CPU 仍使用 lua.cpu。
图格式为 `{format:1,nodes:[...],edges:[...]}`。节点使用稳定 id 和 kind：start、system、player、end。
start 必须唯一且没有入边；end 至少一个且没有出边。结束节点 winners=[] 表示平局。

每个非结束节点声明 results（默认 done），连线定义 id/from/to/result；条件 condition 支持
true/false、eq/ne/lt/le/gt/ge、all/any/not、lua。priority 越大越优先，default 为无匹配时的出口。
同最高优先级多匹配、无出口或封闭循环均给出诊断；不证明复杂 Lua 的终止性。

系统节点 commands 声明标准效果，script={path,entry} 选择 Lua 函数。
函数输入是 state（权威状态）、parameters、gameContext、nodeContext；返回 commands、result，
可替换私有上下文，结束节点可返回 winners。修改游戏只通过命令，不能直接指定下一节点。
系统节点可读取隐藏牌；这些值不能默认进入玩家文本。上下文合计最多 16 KiB，节点上下文每次进入重置。

玩家节点 seat 选择 fixed、current:true 或 script；进入脚本以系统身份执行一次。
actionsScript 返回标准动作数组，decideScript 返回 commands、result、complete。
两者仅收到本席位 view 和 parameters，decide 另带 command；不能读取或修改私有上下文。
complete=false 继续等待，不重跑进入脚本；true 沿图离开。CPU 读取同样投影和动作。
简单节点也可直接用 actions=[{id,label,commands,complete,result}]，无需 Lua。
脚本文件返回函数表，例如 `return { execute=function(x) return {commands={},result='done'} end }`。
条件脚本返回 boolean，席位脚本返回 ID。使用 shuffle／roll 产生可记录随机效果。

CLI（安装目录中的 bgb.exe）可发现以下操作：

```
bgb docs flow
bgb call flow.read --project <工程目录>
bgb call flow.validate --project <工程目录>
bgb describe flow.replace
bgb call project.validate --project <工程目录>
bgb call run.cpu --project <工程目录> --args '{"seed":1,"count":3,"limit":100}'
```

flow.replace 必须传入 flow.read 的 version 和完整 definition，通过当前 IProjectEditMode 提交。
GUI 模式只更新草稿和 Undo，校验、运行、打包不隐式保存。flow.validate 返回节点／连线级诊断，
缺脚本为错误；无效结构可保留草稿。图或脚本变化后重建运行会话。
完整运行时说明见源码发行的 docs/flow-runtime.md；配置示例为 samples/flow-minimal，
Lua 卡牌示例为 samples/flow-cards。发牌、判定、结算与胜负全部在示例配置和脚本中。
