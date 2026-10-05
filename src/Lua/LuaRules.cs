using System.Globalization;
using BoardGame.Kernel;
namespace BoardGame.Lua;
public sealed class LuaRules(string source,string path,uint seed=1) : IGameRules
{
    private string Invoke(string entry,string input)
    {
        var random=uint.Parse(Wire.Hash($"{seed}:{entry}:{input}")[..8],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return LuaWorker.Invoke(source,path,entry,input,random);
    }
    public IReadOnlyList<ActionOffer> Actions(SeatView view) => Wire.Read<List<ActionOffer>>(Invoke("actions",Wire.Json(new { view })));
    public RuleDecision Decide(SeatView view,Command command) => Wire.Read<RuleDecision>(Invoke("decide",Wire.Json(new { view, command })));
}
