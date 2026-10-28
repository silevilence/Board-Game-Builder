using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal sealed class RuleBinding : IGameRules
{
    private IGameRules? rules;
    public void Replace(IGameRules value)=>rules=value;
    public IReadOnlyList<ActionOffer> Actions(SeatView view)=>rules?.Actions(view) ?? [];
    public RuleDecision Decide(SeatView view,Command command)=>rules?.Decide(view,command) ?? new(true,"",[command]);
}

internal sealed class KernelAuthority(GameEngine initialEngine) : IAuthoritySession,IFlowAuthority,IBoardTransactionSource
{
    private GameEngine engine=initialEngine;
    public IBoardStateBranch Begin()=>new Branch(this,engine);
    private sealed class Branch(KernelAuthority owner,GameEngine original) : IBoardStateBranch
    {
        private readonly long revision=original.Revision;
        private readonly KernelAuthority candidate=new(original.Fork());
        private bool used;
        public IAuthoritySession Authority=>new BranchAuthority(this);
        public bool TryCommit()
        {
            if(used) return false;
            used=true;
            if(!ReferenceEquals(owner.engine,original) || original.Revision!=revision) return false;
            owner.engine=candidate.engine;return true;
        }
        private sealed class BranchAuthority(Branch branch) : IAuthoritySession,IFlowAuthority
        {
            private KernelAuthority Active=>!branch.used?branch.candidate:throw new InvalidOperationException("Board branch has already been published or discarded");
            public SeatView Project(string seat)=>Active.Project(seat);
            public bool CanAct(string seat)=>Active.CanAct(seat);
            public IReadOnlyList<ActionOffer> Actions(string seat)=>Active.Actions(seat);
            public CommandResult Submit(string seat,Command command)=>Active.Submit(seat,command);
            public GameState ExportAuthorityState()=>Active.ExportAuthorityState();
            public ReplayLog ExportLog()=>Active.ExportLog();
            public IReadOnlyList<Diagnostic> ExportDiagnostics()=>Active.ExportDiagnostics();
            public CommandResult SubmitFlow(string? actor,Command command,long revision,Action<GameEngine.FlowTransaction> operation)=>Active.SubmitFlow(actor,command,revision,operation);
        }
    }
    public CommandResult SubmitFlow(string? actor,Command command,long revision,Action<GameEngine.FlowTransaction> operation)=>engine.SubmitFlow(actor,command,revision,operation);
    public SeatView Project(string seat)=>engine.Project(seat);
    public bool CanAct(string seat)=>engine.CanAct(seat);
    public IReadOnlyList<ActionOffer> Actions(string seat)=>engine.Actions(seat);
    public CommandResult Submit(string seat,Command command)=>engine.Submit(seat,command);
    public GameState ExportAuthorityState()=>engine.ExportAuthorityState();
    public ReplayLog ExportLog()=>engine.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>engine.ExportDiagnostics();
}

public static class AuthorityPlugin
{
    public const string Access="board.authority";
    public static readonly string[] Required=["authority","projection","random","rule-binding"];
    public static RuntimePluginDefinition Create(Func<LoadedModule> input,uint seed,GameEngine? restored=null)=>new("kernel","1",1,[..Required,"flow-transactions","board-preparation"],[],scope=>
    {
        var module=input();
        var binding=new RuleBinding();
        var authority=new KernelAuthority(restored ?? new GameEngine(module.Initial,seed,module.Fingerprint,binding));
        scope.Provide("authority",authority);
        scope.Provide("projection",new BoardProjectionSource(authority));
        // Random state can only advance inside the existing atomic command transaction.
        scope.Provide("random",new BoardCommandSource(authority));
        scope.Provide("rule-binding",binding);
        scope.Provide("flow-transactions",authority);
        scope.Provide("board-preparation",authority);
    }) {ServicePermissions=new Dictionary<string,string>
        {{"authority",Access},{"projection",Access},{"random",Access},{"rule-binding",Access},{"flow-transactions",Access},{"board-preparation",Access}}};
}

internal sealed class BoardProjectionSource(IAuthoritySession authority) : IBoardProjectionSource
{
    public ISeatProjection ForSeat(string seat)
    {
        _=authority.Project(seat);
        return new SeatProjection(authority,seat);
    }
    private sealed class SeatProjection(IAuthoritySession authority,string seat) : ISeatProjection
    {public SeatView View()=>authority.Project(seat);}
}
internal sealed class BoardCommandSource(IAuthoritySession authority) : IBoardCommandSource
{
    public bool CanAct(string seat)=>authority.CanAct(seat);
    public IReadOnlyList<ActionOffer> Actions(string seat)=>authority.Actions(seat);
    public CommandResult Submit(string seat,Command command)=>authority.Submit(seat,command);
}
