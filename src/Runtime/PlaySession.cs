using System.Globalization;
using BoardGame.Kernel;
using BoardGame.Lua;
namespace BoardGame.Runtime;
public sealed record SessionFault(string Code,string Seat,string Detail);
public sealed class PlaySession : IDisposable
{
    private LoadedModule module;
    private readonly Dictionary<string,LoadedModule> versions=new(StringComparer.Ordinal);
    private readonly List<ScriptSwitch> switches=[];
    private readonly GameEngine engine;
    private readonly Dictionary<string,string> controllers;
    private readonly uint seed;
    private readonly int maxSteps;
    private readonly LuaWorkerSession worker=new();
    private volatile bool disposed;
    public SessionFault? Fault { get; private set; }
    public int Steps { get; private set; }
    public bool Ended => engine.Project(module.Initial.Seats[0].Id).Ended;
    public string CurrentSeat => engine.Project(module.Initial.Seats[0].Id).CurrentSeat;
    public bool IsCpu => controllers[CurrentSeat]=="cpu";
    public PlaySession(LoadedModule module,uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500)
    {
        if(maxSteps<1) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        this.module=Wire.Copy(module);this.seed=seed;this.maxSteps=maxSteps;
        versions.Add(module.Fingerprint,Wire.Copy(module)); switches.Add(new(0,module.Fingerprint));
        this.controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Controller);
        if(controllers!=null) foreach(var c in controllers)
        {
            if(!this.controllers.ContainsKey(c.Key) || c.Value is not ("human" or "cpu")) throw new ArgumentException("Invalid seat/controller configuration.");
            this.controllers[c.Key]=c.Value;
        }
        engine=new GameEngine(module.Initial,seed,module.Fingerprint,new LuaRules(module.Rules,module.RulesPath,seed,worker));
    }
    public SeatView View(string seat) => engine.Project(seat);
    public IReadOnlyList<ActionOffer> Actions()
    {
        if(disposed || Fault!=null || Ended) return [];
        try { return engine.Actions(CurrentSeat); }
        catch(Exception e) { Stop("rule-failure",e.Message);return []; }
    }
    public CommandResult SubmitHuman(string seat,Command command)
    {
        if(disposed || Fault!=null || Ended || seat!=CurrentSeat || controllers[seat]!="human") return new(0,false,"operation-not-allowed");
        return Submit(command,false);
    }
    public bool StepCpu()
    {
        if(disposed || Fault!=null || Ended || !IsCpu) return false;
        var actions=Actions();if(Fault!=null) return false;
        if(actions.Count==0) { Stop("no-actions","Rules did not offer a legal action or pass.");return false; }
        try
        {
            var view=View(CurrentSeat);
            var random=uint.Parse(Wire.Hash($"{seed}:{Steps}:{CurrentSeat}")[..8],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            var json=worker.Invoke(module.Cpu,module.CpuPath,"decide",Wire.Json(new { view,actions }),random);
            var command=Wire.Read<Command>(json);
            if(!actions.Any(a=>Wire.Json(a.Command)==Wire.Json(command)))
            { Stop("cpu-illegal-command","CPU returned a command not offered by the rules.");return false; }
            return Submit(command,true).Accepted;
        }
        catch(Exception e) { Stop("cpu-failure",e.Message);return false; }
    }
    private CommandResult Submit(Command command,bool cpu)
    {
        if(Steps>=maxSteps) { Stop("step-limit",$"Exceeded {maxSteps} steps.");return new(0,false,"step-limit"); }
        var before=Wire.Json(engine.ExportAuthorityState());
        var result=engine.Submit(CurrentSeat,command);
        if(!result.Accepted)
        {
            if(cpu || result.Reason=="script-failure" || result.Reason=="rule-command-limit") Stop(cpu?"cpu-rejected":"rule-failure",result.Reason);
            return result;
        }
        Steps++;
        if(before==Wire.Json(engine.ExportAuthorityState())) Stop("no-progress","Command left state unchanged.");
        if(Ended) worker.Dispose();
        return result;
    }
    private void Stop(string code,string detail) { Fault ??=new(code,CurrentSeat,detail); worker.Dispose(); }
    public ReplayLog ExportAuthorityLog() => engine.ExportLog();
    public IReadOnlyList<Diagnostic> ExportDiagnostics() => engine.ExportDiagnostics();
    public string AuthorityStateHash() => Wire.Hash(Wire.Json(engine.ExportAuthorityState()));
    // The owner serializes this with commands. A successful switch affects the next submission.
    public void ReloadScripts(LoadedModule candidate)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Fault!=null || Ended) throw new InvalidOperationException("Session ended or faulted; restart required.");
        if(Wire.Json(candidate.Initial)!=Wire.Json(module.Initial) || Wire.Json(candidate.ResourceHashes)!=Wire.Json(module.ResourceHashes) || Wire.Json(candidate.RuntimeHashes)!=Wire.Json(module.RuntimeHashes) || candidate.Id!=module.Id)
            throw new InvalidOperationException("Configuration, resources or dependencies changed; restart required.");
        if(candidate.Fingerprint!=BoardGame.Modules.ModuleSnapshot.Fingerprint(candidate)) throw new InvalidDataException("Script version fingerprint mismatch");
        using(var probe=new LuaWorkerSession())
        {
            probe.Invoke(candidate.Rules,candidate.RulesPath,"__validate_rules","{}",seed);
            probe.Invoke(candidate.Cpu,candidate.CpuPath,"__validate_cpu","{}",seed);
        }
        var next=Wire.Copy(candidate);
        engine.ReplaceRules(new LuaRules(next.Rules,next.RulesPath,seed,worker)); module=next;
        versions[next.Fingerprint]=Wire.Copy(next); switches.Add(new(engine.ExportLog().EntryCount,next.Fingerprint));
    }
    public SessionRecording ExportRecording() => SessionRecording.Create(engine.ExportLog(),switches,versions);
    public void Dispose() { disposed=true; worker.Dispose(); }
}
