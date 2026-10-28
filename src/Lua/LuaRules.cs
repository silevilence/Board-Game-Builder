using System.Globalization;
using BoardGame.Kernel;
namespace BoardGame.Lua;
public sealed class LuaRules(string source,string path,uint seed=1,LuaWorkerSession? worker=null,Func<CancellationToken>? cancellation=null) : IGameRules
{
    private string Invoke(string entry,string input)
    {
        var random=uint.Parse(Wire.Hash($"{seed}:{entry}:{input}")[..8],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        if(worker!=null) return worker.Invoke(source,path,entry,input,random,cancellation?.Invoke() ?? default);
        using var temporary=new LuaWorkerSession();return temporary.Invoke(source,path,entry,input,random,cancellation?.Invoke() ?? default);
    }
    public IReadOnlyList<ActionOffer> Actions(SeatView view) => Wire.Read<List<ActionOffer>>(Invoke("actions",Wire.Json(new { view })));
    public RuleDecision Decide(SeatView view,Command command) => Wire.Read<RuleDecision>(Invoke("decide",Wire.Json(new { view, command })));
}
