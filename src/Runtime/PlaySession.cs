using System.Globalization;
using BoardGame.Kernel;
namespace BoardGame.Runtime;
public sealed record SessionFault(string Code,string Seat,string Detail);
public sealed class PlaySession : IGameSession
{
    private readonly RuntimeHost host;
    private readonly IGameSession session;
    public PlaySession(LoadedModule module,uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500,CancellationToken cancellation=default)
    {
        host=RuntimeComposition.Start(module,seed,controllers,maxSteps,cancellation);
        session=host.Service<IGameSession>("session");
    }
    public SessionFault? Fault=>session.Fault;
    public int Steps=>session.Steps;
    public bool Ended=>session.Ended;
    public string CurrentSeat=>session.CurrentSeat;
    public bool IsCpu=>session.IsCpu;
    public bool SupportsCpu=>session.SupportsCpu;
    public SeatView View(string seat)=>session.View(seat);
    public IReadOnlyList<ActionOffer> Actions(string? seat=null)=>session.Actions(seat);
    public CommandResult SubmitHuman(string seat,Command command)=>session.SubmitHuman(seat,command);
    public bool StepCpu()=>session.StepCpu();
    public void CancelFlow()=>session.CancelFlow();
    public void ResumeFlow(CancellationToken cancellation=default)=>session.ResumeFlow(cancellation);
    public void SetController(string seat,string controller)=>session.SetController(seat,controller);
    public ReplayLog ExportAuthorityLog()=>session.ExportAuthorityLog();
    public SessionRecording ExportRecording()=>session.ExportRecording();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>session.ExportDiagnostics();
    public GameState ExportAuthorityState()=>session.ExportAuthorityState();
    public string AuthorityStateHash()=>session.AuthorityStateHash();
    public void ReloadScripts(LoadedModule candidate)=>session.ReloadScripts(candidate);
    public void Dispose()=>host.Dispose();
}

internal sealed class LocalGameSession : IGameSession
{
    private readonly FlowController? flow;
    private SessionFault? fault;
    private readonly object commands=new();
    private LoadedModule module;
    private readonly IRecordingProvider recording;
    private readonly List<Diagnostic> cpuDiagnostics=[];
    private readonly IAuthoritySession engine;
    private readonly RuleBinding ruleBinding;
    private readonly IEffectProvider? effects;
    private readonly Dictionary<string,string> controllers;
    private readonly uint seed;
    private readonly int maxSteps;
    private readonly IRuleProvider rules;
    private readonly ICpuDecisionProvider? cpu;
    private volatile bool disposed;
    public SessionFault? Fault { get=>fault ?? flow?.Fault; private set=>fault=value; }
    public int Steps { get; private set; }
    public bool Ended => engine.Project(module.Initial.Seats[0].Id).Ended;
    public string CurrentSeat => engine.Project(module.Initial.Seats[0].Id).CurrentSeat;
    public bool IsCpu => controllers[CurrentSeat]=="cpu";
    public bool SupportsCpu => cpu!=null;
    internal LocalGameSession(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,int maxSteps,
        IAuthoritySession engine,RuleBinding ruleBinding,IRuleProvider rules,ICpuDecisionProvider? cpu,IEffectProvider? effects,IRecordingProvider recording,FlowController? flow=null)
    {
        if(maxSteps<1) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        this.module=Wire.Copy(module);this.seed=seed;this.maxSteps=maxSteps;
        this.recording=recording;
        this.controllers=new(controllers);this.engine=flow ?? engine;this.ruleBinding=ruleBinding;this.rules=rules;this.cpu=cpu;this.effects=effects;this.flow=flow;
        ruleBinding.Replace(ComposeRules(rules.Rules));
        flow?.Advance();
    }
    public SeatView View(string seat) => engine.Project(seat);
    public IReadOnlyList<ActionOffer> Actions(string? seat=null)
    {lock(commands) return ReadActions(seat);}
    private IReadOnlyList<ActionOffer> ReadActions(string? seat)
    {
        if(disposed || Fault!=null || Ended) return [];
        try { return engine.Actions(seat ?? CurrentSeat); }
        catch(Exception e) { Stop("rule-failure",e.Message);return []; }
    }
    public CommandResult SubmitHuman(string seat,Command command)
    {lock(commands) return SubmitHumanCore(seat,command);}
    private CommandResult SubmitHumanCore(string seat,Command command)
    {
        if(disposed || Fault!=null || Ended || !engine.CanAct(seat) || controllers.GetValueOrDefault(seat)!="human") return new(0,false,"operation-not-allowed");
        return Submit(command,false,seat);
    }
    public bool StepCpu()
    {lock(commands) return StepCpuCore();}
    public void CancelFlow()=>flow?.Cancel();
    public void ResumeFlow(CancellationToken cancellation=default)
    {
        lock(commands)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(fault!=null || flow==null) throw new InvalidOperationException("Session requires restart or has no flow");
            flow.Resume(cancellation);
        }
    }
    public void SetController(string seat,string controller)
    {
        lock(commands)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(!controllers.ContainsKey(seat) || controller is not ("human" or "cpu")) throw new ArgumentException("Invalid seat/controller configuration.");
            if(controller=="cpu" && cpu==null) throw new InvalidOperationException("Missing CPU capability");
            controllers[seat]=controller;
        }
    }
    private bool StepCpuCore()
    {
        if(disposed || Fault!=null || Ended || !IsCpu) return false;
        var actions=Actions();if(Fault!=null) return false;
        if(actions.Count==0) { Stop("no-actions","Rules did not offer a legal action or pass.");return false; }
        try
        {
            var view=View(CurrentSeat);
            var random=uint.Parse(Wire.Hash($"{seed}:{Steps}:{CurrentSeat}")[..8],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            var command=(cpu ?? throw new InvalidOperationException("Missing CPU capability")).Decide(view,actions,random);
            cpuDiagnostics.Add(new(engine.ExportLog().EntryCount+1,CurrentSeat,cpu.Entry,Wire.Json(command)));
            if(!actions.Any(a=>Wire.Json(a.Command)==Wire.Json(command)))
            { Stop("cpu-illegal-command","CPU returned a command not offered by the rules.");return false; }
            return Submit(command,true).Accepted;
        }
        catch(Exception e) { Stop("cpu-failure",e.Message);return false; }
    }
    private CommandResult Submit(Command command,bool cpu,string? actor=null)
    {
        if(disposed) return new(0,false,"operation-not-allowed");
        // The runaway guard is for automatic play; a human may keep thinking, moving and undoing.
        if(cpu && Steps>=maxSteps) { Stop("step-limit",$"Exceeded {maxSteps} steps.");return new(0,false,"step-limit"); }
        var before=Wire.Json(engine.ExportAuthorityState());
        var result=engine.Submit(actor ?? CurrentSeat,command);
        if(!result.Accepted)
        {
            if(flow?.Fault==null && (cpu || result.Reason=="script-failure" || result.Reason=="rule-command-limit")) Stop(cpu?"cpu-rejected":"rule-failure",result.Reason);
            return result;
        }
        Steps++;
        if(before==Wire.Json(engine.ExportAuthorityState())) Stop("no-progress","Command left state unchanged.");
        if(Ended) StopExecution();
        return result;
    }
    private void StopExecution() { (rules as IDisposable)?.Dispose();(cpu as IDisposable)?.Dispose(); }
    private void Stop(string code,string detail) { Fault ??=new(code,CurrentSeat,detail); cpuDiagnostics.Add(new(engine.ExportLog().EntryCount+1,CurrentSeat,cpu?.Entry ?? "session",code+": "+detail)); StopExecution(); }
    public ReplayLog ExportAuthorityLog() => engine.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics() => engine.ExportDiagnostics().Concat(Wire.Copy(cpuDiagnostics)).OrderBy(d=>d.Sequence).ToArray();
    public string AuthorityStateHash() => Wire.Hash(Wire.Json(engine.ExportAuthorityState()));
    public GameState ExportAuthorityState() => engine.ExportAuthorityState();
    // The owner serializes this with commands. A successful switch affects the next submission.
    public void ReloadScripts(LoadedModule candidate)
    {lock(commands) ReloadScriptsCore(candidate);}
    private void ReloadScriptsCore(LoadedModule candidate)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Fault!=null || Ended) throw new InvalidOperationException("Session ended or faulted; restart required.");
        if(module.RuntimeIdentity is {} identity)
        {
            GameRuntime.ValidateSnapshot(candidate);
            var nextIdentity=candidate.RuntimeIdentity!;
            if(Wire.Json(identity.Startup)!=Wire.Json(nextIdentity.Startup) || identity.DefinitionSource!=nextIdentity.DefinitionSource)
                throw new InvalidOperationException("Runtime plugin configuration changed; restart required.");
            var scripts=RuntimeContent.ScriptFiles(identity.Startup).ToHashSet();
            if(Wire.Json(identity.ContentHashes.Where(p=>!scripts.Contains(p.Key)).ToArray())!=Wire.Json(nextIdentity.ContentHashes.Where(p=>!scripts.Contains(p.Key)).ToArray()))
                throw new InvalidOperationException("Non-script content changed; restart required.");
        }
        if(Wire.Json(candidate.Initial)!=Wire.Json(module.Initial) || Wire.Json(candidate.Resources)!=Wire.Json(module.Resources) || Wire.Json(candidate.ResourceHashes)!=Wire.Json(module.ResourceHashes) || Wire.Json(candidate.RuntimeHashes)!=Wire.Json(module.RuntimeHashes) || Wire.Json(candidate.RuntimePlugins)!=Wire.Json(module.RuntimePlugins) || candidate.Id!=module.Id)
            throw new InvalidOperationException("Configuration, resources or dependencies changed; restart required.");
        if(candidate.Fingerprint!=BoardGame.Modules.ModuleSnapshot.Fingerprint(candidate)) throw new InvalidDataException("Script version fingerprint mismatch");
        var definition=RuntimeDefinition.From(candidate);
        var preparedRules=rules.Prepare(definition);
        var applyCpu=cpu?.Prepare(definition);
        var next=Wire.Copy(candidate);
        rules.Apply(preparedRules);applyCpu?.Invoke();ruleBinding.Replace(ComposeRules(rules.Rules));module=next;
        recording.RecordVersion(next);
    }
    public SessionRecording ExportRecording() => recording.Capture();
    private IGameRules ComposeRules(IGameRules value)=>effects==null?value:new EffectRules(value,effects);
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        StopExecution(); // Interrupt workers before waiting for the active command boundary.
        lock(commands) { }
    }
}
