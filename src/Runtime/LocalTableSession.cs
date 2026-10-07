using BoardGame.Kernel;

namespace BoardGame.Runtime;

public enum LocalTableMode { HotSeat, SplitScreen }
public sealed record LocalTableFrame(long Revision,string? WaitingFor,IReadOnlyList<SessionFrame> Views,bool Cpu,bool Ended);

// The authority stays behind this boundary. Each displayed view is a detached seat projection.
public sealed class LocalTableSession : IDisposable
{
    private readonly PlaySession session;
    private readonly string[] humans;
    private readonly LocalTableMode mode;
    private readonly SemaphoreSlim gate=new(1);
    private string? active;
    private long revision;
    private volatile bool disposed;
    public LocalTableFrame Frame {get;private set;}
    public LocalTableSession(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,LocalTableMode mode)
    {
        if(module.Initial.Seats.Any(s=>!controllers.ContainsKey(s.Id))) throw new ArgumentException("Every seat requires a controller.");
        humans=module.Initial.Seats.Where(s=>controllers[s.Id]=="human").Select(s=>s.Id).ToArray();
        if(humans.Length==0) throw new ArgumentException("Local table requires a human seat.");
        this.mode=mode;session=new(module,seed,controllers);Frame=Read();
    }
    private LocalTableFrame Read()
    {
        var cpu=session.IsCpu;var ended=session.Ended;
        var actions=session.Actions();
        if(mode==LocalTableMode.HotSeat && (cpu || active!=session.CurrentSeat)) active=null;
        var waiting=mode==LocalTableMode.HotSeat && active==null && !cpu?session.CurrentSeat:null;
        var seats=mode==LocalTableMode.SplitScreen?humans:active==null?[]:[active];
        var fault=session.Fault is {} f?f with {Detail="规则或 CPU 执行失败，请重新开局。"}:null;
        var views=seats.Select(s=>new SessionFrame(revision,session.View(s),!cpu && s==session.CurrentSeat?actions:[],cpu,fault)).ToArray();
        return new(revision,waiting,views,cpu,ended);
    }
    public async Task<bool> TakeSeatAsync(long expected,string seat)
    {
        await gate.WaitAsync();
        try
        {
            if(disposed || expected!=revision || Frame.WaitingFor!=seat) return false;
            active=seat;revision++;Frame=Read();return true;
        }
        finally {gate.Release();}
    }
    public async Task<CommandResult> SubmitAsync(long expected,string seat,InteractionIntent intent)
    {
        await gate.WaitAsync();
        try
        {
            var view=Frame.Views.FirstOrDefault(v=>v.View.Seat==seat);
            if(disposed || expected!=revision || view==null || view.View.CurrentSeat!=seat || Frame.Cpu || Frame.Ended)
                return new(0,false,"stale-or-inactive");
            var result=await Task.Run(()=>session.SubmitHuman(seat,PlayerSession.Resolve(view.Actions,intent)));
            if(!disposed) {revision++;Frame=Read();}return result;
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
            if(!disposed) {revision++;Frame=Read();}return result;
        }
        finally {gate.Release();}
    }
    public void Dispose() {disposed=true;session.Dispose();Frame=new(++revision,null,[],false,true);}
}
