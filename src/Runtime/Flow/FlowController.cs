using BoardGame.Kernel;
using BoardGame.Lua;
using System.Text.Json;

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
    private readonly LuaWorkerSession worker=new();
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
            if(state.Ended || Fault!=null)return;
            if(flow.Status=="waiting") {CheckActions(flow);return;}
            var node=nodes[flow.Node];
            var result=transactions.SubmitFlow(null,new(){Kind="flow-enter",Entity=node.Id},flow.Revision,tx=>Enter(tx,node,flow));
            if(!result.Accepted) {Fault=new("flow-execution", "", "Node "+node.Id+": "+result.Reason);return;}
        }
        if(authority.ExportAuthorityState().Flow is {Status:"ready"} current) Fault=new("flow-budget","","Automatic step budget exhausted at node "+current.Node);
        else if(authority.ExportAuthorityState().Flow is {Status:"waiting"} waiting) CheckActions(waiting);
    }
    private void CheckActions(FlowState flow)
    {
        try {if(Actions(flow.Seat!).Count==0) Fault=new("flow-no-actions",flow.Seat!,"No legal action at node "+flow.Node);}
        catch(Exception e) when(e is not OutOfMemoryException) {Fault=new("flow-actions-error",flow.Seat!,"Action query failed at node "+flow.Node);}
    }
    private void Enter(GameEngine.FlowTransaction tx,FlowNode node,FlowState flow)
    {
        var next=flow with {Revision=flow.Revision+1,NodeContext=new(StringComparer.Ordinal)};
        foreach(var command in node.Commands) tx.Apply(command);
        var outcome=node.Script==null?new FlowOutcome():Invoke<FlowOutcome>(node.Script,Input(tx.Snapshot(),node,next),tx.Snapshot().RandomState);
        next=ApplyOutcome(tx,node,next,outcome);
        if(node.Kind=="player")
        {
            var state=tx.Snapshot();var selector=node.Seat!;
            var seat=selector.Fixed ?? (selector.Current?state.Seats[state.CurrentSeat].Id:Invoke<string>(selector.Script!,Input(state,node,next),state.RandomState));
            if(!state.Seats.Any(s=>s.Id==seat)) throw new FlowOperationException("flow-invalid-seat");
            tx.Complete(next with {Status="waiting",Seat=seat});
        }
        else if(node.Kind=="end") tx.Complete(next with {Status="ended",Result=outcome.Result,Seat=null,
            Trace=next.Trace.Append(new(next.Revision,node.Id,outcome.Result,null)).TakeLast(64).ToList()},outcome.Winners ?? node.Winners ?? throw new InvalidDataException("flow-end-result"));
        else tx.Complete(Leave(tx.Snapshot(),node,next,outcome.Result));
    }
    private FlowState ApplyOutcome(GameEngine.FlowTransaction tx,FlowNode node,FlowState next,FlowOutcome outcome)
    {
        if(outcome.Commands==null || !node.Results.Contains(outcome.Result) || node.Kind!="end" && outcome.Winners!=null) throw new FlowOperationException("flow-outcome");
        foreach(var command in outcome.Commands) tx.Apply(command);
        return next with {GameContext=outcome.GameContext ?? next.GameContext,NodeContext=outcome.NodeContext ?? next.NodeContext};
    }
    private static object Input(GameState state,FlowNode node,FlowState flow)=>new {state,parameters=node.Parameters,gameContext=flow.GameContext,nodeContext=flow.NodeContext};
    private T Invoke<T>(FlowScript script,object input,uint random)
    {
        try {return Wire.Read<T>(worker.Invoke(content.Scripts[script.Path],script.Path,script.Entry,Wire.Json(input),random));}
        catch(Exception e) when(e is not OutOfMemoryException) {throw new FlowOperationException("flow-script-error");}
    }
    private FlowState Leave(GameState state,FlowNode node,FlowState flow,string result)
    {
        var edges=content.Definition.Edges.Where(e=>e.From==node.Id && e.Result==result).ToArray();
        var input=Input(state,node,flow);var json=JsonSerializer.SerializeToElement(input,Wire.Options);
        var matched=edges.Where(e=>!e.Default && FlowConditions.Evaluate(e.Condition,p=>FlowConditions.Resolve(json,p),script=>
            FlowConditions.Constant(new("lua",Script:script),content.Scripts)==false?false:Invoke<bool>(script,input,state.RandomState))==true).OrderByDescending(e=>e.Priority).ToArray();
        var edge=matched.FirstOrDefault() ?? edges.SingleOrDefault(e=>e.Default) ?? throw new FlowOperationException("flow-no-branch");
        if(matched.Count(e=>e.Priority==edge.Priority)>1) throw new FlowOperationException("flow-ambiguous-branch");
        return flow with {Node=edge.To,Status="ready",Seat=null,Result=result,Edge=edge.Id,NodeContext=new(StringComparer.Ordinal),
            Trace=flow.Trace.Append(new(flow.Revision,node.Id,result,edge.Id)).TakeLast(64).ToList()};
    }
    public SeatView Project(string seat)=>authority.Project(seat);
    public bool CanAct(string seat)=>Fault==null && authority.CanAct(seat);
    public IReadOnlyList<ActionOffer> Actions(string seat)
    {
        if(!CanAct(seat))return [];
        var state=authority.ExportAuthorityState();var flow=state.Flow!;var node=nodes[flow.Node];
        var offers=node.ActionsScript==null?node.Actions.Select(a=>new ActionOffer(a.Id,a.Label,new(){Kind="flow-action",Entity=a.Id})).ToList():
            Invoke<List<ActionOffer>>(node.ActionsScript,new {view=authority.Project(seat),parameters=node.Parameters},state.RandomState);
        if(offers.Count>256 || offers.Any(a=>a==null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Label) || a.Command==null || string.IsNullOrWhiteSpace(a.Command.Kind)) || offers.Select(a=>a.Id).Distinct().Count()!=offers.Count)
            throw new FlowOperationException("flow-action-schema");
        return offers.Select(a=>a with {Command=a.Command with {FlowRevision=flow.Revision}}).ToList();
    }
    public CommandResult Submit(string seat,Command command)
    {
        if(!CanAct(seat))return new(0,false,"operation-not-allowed");
        var state=authority.ExportAuthorityState();var flow=state.Flow!;var node=nodes[flow.Node];
        if(command.FlowRevision!=flow.Revision || !Actions(seat).Any(a=>Wire.Json(a.Command)==Wire.Json(command))) return new(0,false,"flow-stale-or-illegal-action");
        var result=transactions.SubmitFlow(seat,command,flow.Revision,tx=>
        {
            FlowOutcome outcome;
            if(node.DecideScript==null)
            {
                var action=node.Actions.Single(a=>a.Id==command.Entity);
                outcome=new(){Commands=action.Commands,Result=action.Result,Complete=action.Complete};
            }
            else outcome=Invoke<FlowOutcome>(node.DecideScript,new {view=authority.Project(seat),parameters=node.Parameters,command=command with {FlowRevision=null}},state.RandomState);
            if(outcome.GameContext!=null || outcome.NodeContext!=null || outcome.Winners!=null) throw new FlowOperationException("flow-player-context-denied");
            var next=ApplyOutcome(tx,node,flow with {Revision=flow.Revision+1},outcome);
            tx.Complete(outcome.Complete?Leave(tx.Snapshot(),node,next,outcome.Result):next);
        });
        if(result.Accepted)Advance();
        else if(result.Reason.StartsWith("flow-",StringComparison.Ordinal)) Fault=new("flow-execution",seat,"Node "+node.Id+": "+result.Reason);
        return result;
    }
    public GameState ExportAuthorityState()=>authority.ExportAuthorityState();
    public ReplayLog ExportLog()=>authority.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>authority.ExportDiagnostics();
    public IGameRules Rules=>this;
    public IGameRules Prepare(GameDefinition definition)=>throw new InvalidOperationException("Flow content changed; restart required");
    public void Apply(IGameRules rules)=>throw new InvalidOperationException("Flow content changed; restart required");
    IReadOnlyList<ActionOffer> IGameRules.Actions(SeatView view)=>[];
    public RuleDecision Decide(SeatView view,Command command)=>new(false,"flow-owned",[]);
    public void Dispose()=>worker.Dispose();
}
