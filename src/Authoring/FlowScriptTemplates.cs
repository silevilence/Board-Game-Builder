namespace BoardGame.Authoring;

// Templates and guidance share the runtime contract; they do not implement game rules.
public static class FlowScriptTemplates
{
    public static string Text(string role) => role switch
    {
        "execute" => "-- ctx: state, parameters, gameContext, nodeContext\n-- Return controlled commands and a named result; never a target node ID.\nreturn { execute = function(ctx)\n  return { commands = {}, result = 'done' }\nend }\n",
        "seat" => "-- Return a stable seat ID from the authoritative state.\nreturn { seat = function(ctx)\n  return ctx.state.seats[ctx.state.currentSeat + 1].id\nend }\n",
        "actions" => "-- ctx.view contains only this seat's projection.\nreturn { actions = function(ctx)\n  return {{ id = 'done', label = 'Done', command = { kind = 'pass' } }}\nend }\n",
        "decide" => "-- Validate ctx.command; complete=false keeps this node waiting.\nreturn { decide = function(ctx)\n  if ctx.command.kind ~= 'pass' then error('illegal action') end\n  return { commands = {}, complete = true, result = 'done' }\nend }\n",
        _ => throw new ArgumentException("Unknown flow script role")
    };
    public const string Help = "FlowNode: results:string[]；commands:Command[]；parameters:object；script:{path,entry}。\n玩家 seat:{fixed:席位ID} / {current:true} / {script:{path,entry}} 三选一；actions:FlowAction[] 中 complete=false 继续等待，true 按 result 离开；动态动作需 actionsScript 与 decideScript 成对配置。\n结束 winners:席位ID[]，空数组表示平局。FlowEdge: result:string、priority:int（大者优先）、default:bool、condition:{op,left,right,items,script}；op 支持 true/false/eq/ne/lt/le/gt/ge/all/any/not/lua。条件 path 相对节点输入，例如 /state/turn、/parameters/limit。\n引用实体、容器与区域使用稳定 ID；复杂条件的终止性由作者负责。修改脚本后显式重开试玩。";
}
