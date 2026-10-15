using BoardGame.Kernel;

namespace BoardGame.Runtime;

internal interface IFlowAuthority
{
    CommandResult SubmitFlow(string? actor,Command command,long revision,Action<GameEngine.FlowTransaction> operation);
}

internal sealed class FlowController : IAuthoritySession,IRuleProvider,IGameRules,IDisposable
{
    private readonly IAuthoritySession authority;
    private readonly IFlowAuthority transactions;
    private readonly FlowContent content;
    private readonly Dictionary<string,FlowNode> nodes;
    private readonly int budget;
    public SessionFault? Fault {get;private set;}
    public FlowController(IAuthoritySession authority,IFlowAuthority transactions,FlowContent content,int budget)
    {
        this.authority=authority;this.transactions=transactions;this.content=content;this.budget=budget;
        nodes=content.Definition.Nodes.ToDictionary(n=>n.Id);
    }
    public static RuntimePluginDefinition Plugin(Func<LoadedModule> module)=>new("flow","1",1,["rules","flow"],["authority","flow-transactions"],scope=>
    {
        var input=module();var content=FlowContent.Read(input.RuntimeIdentity!.Startup,input.FlowFiles!);
        var budget=scope.Configuration.TryGetProperty("maxAutomaticSteps",out var value)?value.GetInt32():256;
        var controller=scope.Own(new FlowController(scope.Service<IAuthoritySession>("authority"),scope.Service<IFlowAuthority>("flow-transactions"),content,budget));
        scope.Provide("flow",controller);scope.Provide("rules",controller);
    });
    public void Advance()
    {
        for(var step=0;step<budget;step++)
        {
            var state=authority.ExportAuthorityState();var flow=state.Flow!;
            if(state.Ended || flow.Status=="waiting" || Fault!=null)return;
            var node=nodes[flow.Node];
            var result=transactions.SubmitFlow(null,new(){Kind="flow-enter",Entity=node.Id},flow.Revision,tx=>Enter(tx,node,flow));
            if(!result.Accepted) {Fault=new("flow-execution", "", "Node "+node.Id+": "+result.Reason);return;}
        }
        if(authority.ExportAuthorityState().Flow is {Status:"ready"} current) Fault=new("flow-budget","","Automatic step budget exhausted at node "+current.Node);
    }
    private void Enter(GameEngine.FlowTransaction tx,FlowNode node,FlowState flow)
    {
        foreach(var command in node.Commands) tx.Apply(command);
        if(node.Script!=null || node.Kind=="player") throw new InvalidDataException("flow: script/player execution is unavailable");
        var next=flow with {Revision=flow.Revision+1,NodeContext=new(StringComparer.Ordinal)};
        if(node.Kind=="end") tx.Complete(next with {Status="ended",Result="done",Seat=null},node.Winners!);
        else tx.Complete(Leave(node,next,"done"));
    }
    private FlowState Leave(FlowNode node,FlowState flow,string result)
    {
        var edges=content.Definition.Edges.Where(e=>e.From==node.Id && e.Result==result).ToArray();
        var matched=edges.Where(e=>!e.Default && FlowConditions.Constant(e.Condition,content.Scripts)==true).OrderByDescending(e=>e.Priority).ToArray();
        var edge=matched.FirstOrDefault() ?? edges.SingleOrDefault(e=>e.Default) ?? throw new InvalidDataException("flow-no-branch");
        if(matched.Count(e=>e.Priority==edge.Priority)>1) throw new InvalidDataException("flow-ambiguous-branch");
        return flow with {Node=edge.To,Status="ready",Seat=null,Result=result,Edge=edge.Id,NodeContext=new(StringComparer.Ordinal),
            Trace=flow.Trace.Append(new(flow.Revision,node.Id,result,edge.Id)).TakeLast(64).ToList()};
    }
    public SeatView Project(string seat)=>authority.Project(seat);
    public bool CanAct(string seat)=>Fault==null && authority.CanAct(seat);
    public IReadOnlyList<ActionOffer> Actions(string seat)=>[];
    public CommandResult Submit(string seat,Command command)=>new(0,false,"operation-not-allowed");
    public GameState ExportAuthorityState()=>authority.ExportAuthorityState();
    public ReplayLog ExportLog()=>authority.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>authority.ExportDiagnostics();
    public IGameRules Rules=>this;
    public IGameRules Prepare(GameDefinition definition)=>throw new InvalidOperationException("Flow content changed; restart required");
    public void Apply(IGameRules rules)=>throw new InvalidOperationException("Flow content changed; restart required");
    IReadOnlyList<ActionOffer> IGameRules.Actions(SeatView view)=>[];
    public RuleDecision Decide(SeatView view,Command command)=>new(false,"flow-owned",[]);
    public void Dispose() { }
}
