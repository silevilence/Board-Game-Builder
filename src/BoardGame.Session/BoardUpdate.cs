using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

public sealed record BoardUpdateRequest([property:JsonRequired] long Revision,string? Seat=null,Command? Command=null,string? Controller=null);
public sealed record BoardUpdateResult(long Sequence,bool Accepted,string Reason,long CommandSequence=0);
public interface IBoardUpdateState
{
    IReadOnlyList<BoardUpdateResult> UpdateResults {get;}
}

internal sealed partial class LocalGameSession
{
    private readonly IBoardTransactionSource preparation;
    private readonly bool staged;
    private bool preparing;
    private long generation;
    private IFlowProgression? activeFlow;
    private Dictionary<string,List<ActionOffer>> actionCache=new(StringComparer.Ordinal);
    private IReadOnlyList<BoardUpdateResult> updateResults=[];
    public IReadOnlyList<BoardUpdateResult> UpdateResults {get {lock(commands) return updateResults.ToArray();}}
    private void BeginDirectMutation()
    {
        if(preparing) throw new InvalidOperationException("A board update is being prepared; enqueue input for the next update boundary");
        generation++;
    }
    private LocalGameSession(LocalGameSession source,IAuthoritySession authority,IFlowProgression? candidateFlow)
    {
        content=source.content;
        staged=true;preparation=source.preparation;module=source.module;recording=source.recording;
        seed=source.seed;maxSteps=source.maxSteps;ruleBinding=source.ruleBinding;
        rules=source.rules;cpu=source.cpu;effects=source.effects;
        flow=candidateFlow;engine=candidateFlow ?? authority;fault=source.fault;Steps=source.Steps;
        controllers=new(source.controllers);cpuDiagnostics=new(source.cpuDiagnostics);
    }
    public ValueTask<Action?> PrepareUpdate(UpdateFrame frame,CancellationToken cancellation)
    {
        var inputs=frame.Inputs.Where(i=>i.Kind.StartsWith("board.",StringComparison.Ordinal)).ToArray();
        IBoardStateBranch branch;IPreparedFlow? preparedFlow;LocalGameSession candidate;long expectedGeneration;
        lock(commands)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(preparing) throw new InvalidOperationException("Concurrent board preparation");
            if(inputs.Length==0 && (Fault!=null || Ended || !IsCpu)) return ValueTask.FromResult<Action?>(null);
            branch=preparation.Begin();preparedFlow=flow?.Prepare(branch,cancellation);
            candidate=new(this,branch.Authority,preparedFlow?.Value);expectedGeneration=generation;
            preparing=true;activeFlow=preparedFlow?.Value;
        }
        try
        {
            using var ruleCancellation=(rules as ICancellableExecution)?.Enter(cancellation);
            using var cpuCancellation=(cpu as ICancellableExecution)?.Enter(cancellation);
            var results=new List<BoardUpdateResult>();
            var revision=branch.Authority.ExportLog().EntryCount;
            foreach(var input in inputs)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var request=JsonSerializer.Deserialize(input.Data.Span,Wire.Type<BoardUpdateRequest>(BoardSessionJson.Default))
                        ?? throw new InvalidDataException("Missing board request");
                    if(request.Revision!=revision) {results.Add(new(input.Sequence,false,"stale-revision"));continue;}
                    switch(input.Kind)
                    {
                        case "board.command" when request.Seat!=null && request.Command!=null:
                            var result=candidate.SubmitHumanCore(request.Seat,request.Command);
                            results.Add(new(input.Sequence,result.Accepted,result.Reason,result.Sequence));break;
                        case "board.controller" when request.Seat!=null && request.Controller!=null:
                            candidate.SetController(request.Seat,request.Controller);results.Add(new(input.Sequence,true,"accepted"));break;
                        case "board.resume":
                            candidate.ResumeFlow(cancellation);results.Add(new(input.Sequence,true,"accepted"));break;
                        default: results.Add(new(input.Sequence,false,"invalid-board-request"));break;
                    }
                    revision=branch.Authority.ExportLog().EntryCount;
                }
                catch(Exception error) when(error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
                {results.Add(new(input.Sequence,false,"invalid-board-request"));}
            }
            cancellation.ThrowIfCancellationRequested();
            // An input tick and an automatic step are distinct board operations.
            // Human waiting and render never run a decision.
            if(inputs.Length==0 && candidate.Fault==null && !candidate.Ended && candidate.IsCpu) candidate.StepCpuCore();
            cancellation.ThrowIfCancellationRequested();
            var publishFlow=preparedFlow?.Seal();
            var detachedResults=results.TakeLast(256).ToArray();
            return ValueTask.FromResult<Action?>(()=>
            {
                lock(commands)
                {
                    if(disposed || generation!=expectedGeneration || !branch.TryCommit())
                    {
                        updateResults=detachedResults.Select(r=>r with {Accepted=false,Reason="superseded"}).ToArray();return;
                    }
                    publishFlow?.Invoke();fault=candidate.fault;Steps=candidate.Steps;
                    controllers=candidate.controllers;cpuDiagnostics=candidate.cpuDiagnostics;actionCache=[];
                    updateResults=detachedResults;generation++;
                }
            });
        }
        finally
        {
            preparedFlow?.Dispose();
            lock(commands) {preparing=false;activeFlow=null;}
        }
    }
}
