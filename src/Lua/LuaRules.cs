using BoardGame.Kernel;
namespace BoardGame.Lua;
public sealed class LuaRules(string source,string path) : IGameRules
{
    public IReadOnlyList<ActionOffer> Actions(SeatView view) => Wire.Read<List<ActionOffer>>(LuaEvaluator.Invoke(source,path,"actions",Wire.Json(new { view })));
    public RuleDecision Decide(SeatView view,Command command) => Wire.Read<RuleDecision>(LuaEvaluator.Invoke(source,path,"decide",Wire.Json(new { view, command })));
}
