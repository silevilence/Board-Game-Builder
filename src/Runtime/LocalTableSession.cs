using BoardGame.Kernel;

namespace BoardGame.Runtime;

public enum LocalTableMode { HotSeat, SplitScreen }
public sealed record LocalTableFrame(long Revision,string? WaitingFor,IReadOnlyList<SessionFrame> Views,bool Cpu,bool Ended,SessionFault? Fault=null,IReadOnlyList<string>? CanTake=null,IReadOnlyList<string>? Winners=null);

// The authority stays behind this boundary. Each displayed view is a detached seat projection.
public sealed class LocalTableSession : IDisposable
{
    private readonly IGameSession session;
    private readonly string[] humans;
    private readonly LocalTableMode mode;
    private readonly SemaphoreSlim gate=new(1);
    private string? active;
    private string? pending;
    private long revision;
    private volatile bool disposed;
    public LocalTableFrame Frame {get;private set;}
    public LocalTableSession(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,LocalTableMode mode)
    {
        if(module.Initial.Seats.Any(s=>!controllers.ContainsKey(s.Id))) throw new ArgumentException("Every seat requires a controller.");
        humans=module.Initial.Seats.Where(s=>controllers[s.Id]=="human").Select(s=>s.Id).ToArray();
        this.mode=mode;session=new PlaySession(module,seed,controllers);Frame=Read();
    }
    private LocalTableFrame Read()
    {
        var cpu=session.IsCpu;var ended=session.Ended;
        _=session.Actions();
        if(humans.Length==0)
        {
            var view=GameEngine.ProjectAudienceSnapshot(session.ExportAuthorityState());
            var publicFault=session.Fault is {} failure?failure with {Detail="规则或 CPU 执行失败，请重新开局。"}:null;
            return new(revision,null,[new(revision,view,[],cpu,publicFault)],cpu,ended,publicFault,[],view.Winners);
        }
        var offers=humans.ToDictionary(s=>s,s=>session.Actions(s));
        if(mode==LocalTableMode.HotSeat && (cpu || active!=null && offers[active].Count==0 && !ended)) active=null;
        var waiting=mode==LocalTableMode.HotSeat && active==null && !cpu?pending ?? session.CurrentSeat:null;
        var seats=mode==LocalTableMode.SplitScreen?humans:active==null?[]:[active];
        var fault=session.Fault is {} f?f with {Detail="规则或 CPU 执行失败，请重新开局。"}:null;
        var views=seats.Select(s=>new SessionFrame(revision,session.View(s),!cpu?offers[s]:[],cpu,fault)).ToArray();
        return new(revision,waiting,views,cpu,ended,fault,mode==LocalTableMode.HotSeat && !cpu?offers.Where(p=>p.Value.Count>0).Select(p=>p.Key).ToArray():[],ended?session.View(humans[0]).Winners:[]);
    }
    private async Task Refresh()
    {
        var next=await Task.Run(Read);if(!disposed) Frame=next;
    }
    public async Task<bool> HandoffAsync(long expected,string seat)
    {
        await gate.WaitAsync();
        try
        {
            if(disposed || expected!=revision || Frame.CanTake?.Contains(seat)!=true) return false;
            active=null;pending=seat;revision++;await Refresh();return true;
        }
        finally {gate.Release();}
    }
    public async Task<bool> TakeSeatAsync(long expected,string seat)
    {
        await gate.WaitAsync();
        try
        {
            if(disposed || expected!=revision || Frame.WaitingFor!=seat) return false;
            active=seat;pending=null;revision++;await Refresh();return true;
        }
        finally {gate.Release();}
    }
    public async Task<CommandResult> SubmitAsync(long expected,string seat,InteractionIntent intent)
    {
        await gate.WaitAsync();
        try
        {
            var view=Frame.Views.FirstOrDefault(v=>v.View.Seat==seat);
            if(disposed || expected!=revision || view==null || view.Actions.Count==0 || Frame.Cpu || Frame.Ended)
                return new(0,false,"stale-or-inactive");
            var previous=session.CurrentSeat;
            var result=await Task.Run(()=>session.SubmitHuman(seat,PlayerSession.Resolve(view.Actions,intent)));
            if(!disposed) {if(previous!=session.CurrentSeat) {active=null;pending=null;}revision++;await Refresh();}return result;
        }
        finally {gate.Release();}
    }
    public async Task<bool> StepCpuAsync()
    {
        await gate.WaitAsync();
        try
        {
            if(disposed || !Frame.Cpu || Frame.Ended) return false;
            var result=await Task.Run(session.StepCpu);
            if(!disposed) {revision++;await Refresh();}return result;
        }
        finally {gate.Release();}
    }
    public void Dispose() {disposed=true;session.Dispose();Frame=new(++revision,null,[],false,true);}
}
