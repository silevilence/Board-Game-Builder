using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal sealed class RuleBinding : IGameRules
{
    private IGameRules? rules;
    public void Replace(IGameRules value)=>rules=value;
    public IReadOnlyList<ActionOffer> Actions(SeatView view)=>rules?.Actions(view) ?? [];
    public RuleDecision Decide(SeatView view,Command command)=>rules?.Decide(view,command) ?? new(true,"",[command]);
}

internal sealed class KernelAuthority(GameEngine engine) : IAuthoritySession
{
    public SeatView Project(string seat)=>engine.Project(seat);
    public bool CanAct(string seat)=>engine.CanAct(seat);
    public IReadOnlyList<ActionOffer> Actions(string seat)=>engine.Actions(seat);
    public CommandResult Submit(string seat,Command command)=>engine.Submit(seat,command);
    public GameState ExportAuthorityState()=>engine.ExportAuthorityState();
    public ReplayLog ExportLog()=>engine.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>engine.ExportDiagnostics();
}

internal static class AuthorityPlugin
{
    public static readonly string[] Required=["authority","projection","random","rule-binding"];
    public static RuntimePluginSelection Selection=>new("kernel","1",1,JsonSerializer.SerializeToElement(new {}));
    public static RuntimePluginDefinition Create(LoadedModule module,uint seed)=>new("kernel","1",1,Required,[],scope=>
    {
        var binding=new RuleBinding();
        var authority=new KernelAuthority(new GameEngine(module.Initial,seed,module.Fingerprint,binding));
        scope.Provide("authority",authority);
        scope.Provide("projection",authority);
        scope.Provide("random",authority);
        scope.Provide("rule-binding",binding);
    });
}
