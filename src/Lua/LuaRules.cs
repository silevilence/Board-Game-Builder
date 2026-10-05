using BoardGame.Kernel;
namespace BoardGame.Lua;
public sealed class LuaRules(string source,string path) : IGameRules
{
    public IReadOnlyList<ActionOffer> Actions(SeatView view) => Wire.Read<List<ActionOffer>>(LuaWorker.Invoke(source,path,"actions",Wire.Json(new { view })));
    public RuleDecision Decide(SeatView view,Command command) => Wire.Read<RuleDecision>(LuaWorker.Invoke(source,path,"decide",Wire.Json(new { view, command })));
}
