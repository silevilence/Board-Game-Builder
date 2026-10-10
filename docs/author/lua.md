# Lua 规则与 CPU API

入口插件 lua.rules 和 lua.cpu 的 configuration.file 分别选择脚本。脚本返回函数表，每次调用创建独立脚本环境；持久状态通过命令存入实体整数属性，不依赖 Lua 全局变量。

规则 `actions(ctx)` 返回 `{id,label,command}` 数组，`decide(ctx)` 返回 `{allowed,reason,commands}`。两者读取 ctx.view，decide 还读取 ctx.command。CPU `decide(ctx)` 读取 ctx.view 和 ctx.actions，返回一个允许动作的 command。

ctx.view 包含 seat、seats、currentSeat、phase、turn、ended、winners、entities（数组）、可选 layout/secret。实体投影包含 id、owner、container、isContainer、count、faceUp、face、x、y、properties、contents、resource。隐藏身份和内容不会出现；CPU 不能访问权威状态或其他席位隐藏牌。

可替换模板 rules.lua 的最小规则：

```lua
return {
  actions = function(ctx)
    return {{id='finish', label='完成', command={kind='finish'}}}
  end,
  decide = function(ctx)
    if ctx.command.kind ~= 'finish' then
      return {allowed=false, reason='unknown action', commands={}}
    end
    return {allowed=true, reason='', commands={{kind='finish', winners={ctx.view.seat}}}}
  end
}
```

cpu.lua：

```lua
return {decide=function(ctx) return ctx.actions[1].command end}
```

常用基础命令（实际结构条件仍由内核检查）：

| kind | 参数与作用 |
| --- | --- |
| set | entity、property、value：设置整数属性 |
| move | entity、x、y：移动位置 |
| flip | entity、value：选择从 0 开始的牌面索引，并翻转 faceUp |
| reveal | entity、visibility：设置可见性 |
| transfer | entity、target：移入容器 |
| draw | entity（有序来源容器）、target：转移顶端一张；visibility 可选，默认 owner |
| shuffle | entity（有序容器），使用受控随机 |
| roll | entity：写入 value 属性，骰子值由其声明决定 |
| resize-container | entity、value（新容量） |
| next | 按席位轮转 |
| phase | phase：切换到已声明阶段 |
| finish | winners：胜者席位数组，空数组为无胜者 |

规则可以将游戏专用动作改写为基础命令，最多 64 条，一批全部经过结构与可见性检查，失败不提交半批。CPU 必须返回获得的动作命令之一。没有动作、无进展、脚本异常或超限会成为故障；不要将这些故障当作正常终局。

脚本不能读写文件、网络、宿主对象，不开放动态加载或 math.random。`random_index(n)` 返回受种子控制的 1..n。Lua worker 有指令、时间、内存和输入输出预算；运行诊断包含脚本路径与入口。

`project.validate` 的 runtime 层执行正式加载、初始动作查询及有 CPU 时的首个 CPU 动作检查，不等于全局玩法证明。`run.cpu` 指定种子、局数和步数上限，并检查重复运行与回放；`debug.start/inspect/step/record` 用于逐步定位，见 `docs debugging`。
