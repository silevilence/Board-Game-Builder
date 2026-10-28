using BoardGame.Kernel;
namespace BoardGame.Runtime;

public sealed record SessionFrame(long Revision,SeatView View,IReadOnlyList<ActionOffer> Actions,bool Cpu,SessionFault? Fault);

// One owner for each PlaySession. UI code keeps the last immutable projection during worker execution.
public sealed class PlayerSession : IDisposable
{
    private readonly PlaySession session;
    private readonly string observer;
    private readonly RuntimeHost inputHost;
    private readonly IBoardInteraction interaction;
    private long inputSequence;
    private int busy;
    private volatile bool stopped;
    public bool Busy => Volatile.Read(ref busy)!=0;
    public SessionFrame Frame { get; private set; }
    public string LastMessage { get; private set; }="";
    private PlayerSession(PlaySession session,string observer)
    {
        this.session=session; this.observer=observer; Frame=ReadFrame(0);
        inputHost=BoardInteractionPlugin.StartAsync(new InputTarget(this)).GetAwaiter().GetResult();
        interaction=inputHost.Service<IBoardInteraction>(BoardInteractionPlugin.Capability);
    }
    public static Task<PlayerSession> CreateAsync(LoadedModule module,uint seed,string humanSeat) => Task.Run(()=>
    {
        if(!module.Initial.Seats.Any(s=>s.Id==humanSeat)) throw new ArgumentException("Unknown human seat.");
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Id==humanSeat?"human":"cpu");
        var session=new PlaySession(module,seed,controllers);
        try { return new PlayerSession(session,humanSeat); }
        catch { session.Dispose(); throw; }
    });
    private SessionFrame ReadFrame(long revision)
    {
        var actions=session.Actions();
        // Never pass a CPU's private action list to the observing human.
        var fault=session.Fault;
        if(fault!=null) fault=fault with { Detail="规则或 CPU 执行失败。请结束对局并重新开始；详细诊断仅供作者调试。" };
        if(fault==null && !session.Ended && actions.Count==0) fault=new("no-actions",session.CurrentSeat,"规则没有提供可执行动作，请结束对局或修正规则后重开。");
        return new(revision,session.View(observer),session.IsCpu?[]:actions,session.IsCpu,fault);
    }
    public Task<CommandResult> SubmitAsync(long revision,InteractionIntent intent)=>
        interaction.SubmitAsync(Interlocked.Increment(ref inputSequence),revision,intent);
    private sealed class InputTarget(PlayerSession owner) : IBoardInteractionTarget
    {
        public InteractionFrame Read()=>new(owner.Frame.Revision,owner.Frame.Actions,
            !owner.stopped && !owner.Frame.Cpu && !owner.Frame.View.Ended && owner.Frame.Fault==null);
        public Task<CommandResult> SubmitAsync(long revision,Command command,CancellationToken cancellation)=>owner.SubmitCommandAsync(revision,command,cancellation);
    }
    private async Task<CommandResult> SubmitCommandAsync(long revision,Command command,CancellationToken cancellation)
    {
        if(Interlocked.CompareExchange(ref busy,1,0)!=0) return new(0,false,"busy");
        try
        {
            if(stopped || revision!=Frame.Revision || Frame.Cpu || Frame.View.Ended || Frame.Fault!=null) return new(0,false,"stale-or-inactive");
            var result=await session.SubmitHumanAsync(observer,command,cancellation);
            LastMessage=result.Accepted?"操作已提交":result.Reason=="rule-rejected"?"当前规则不允许此操作，请使用可用动作。":$"操作被拒绝：{result.Reason}";
            if(!stopped) Frame=await Task.Run(()=>ReadFrame(Frame.Revision+1));
            return result;
        }
        finally { Volatile.Write(ref busy,0); }
    }
    public async Task<bool> StepCpuAsync()
    {
        if(Interlocked.CompareExchange(ref busy,1,0)!=0) return false;
        try
        {
            if(stopped || !Frame.Cpu || Frame.View.Ended || Frame.Fault!=null) return false;
            var result=await session.StepCpuAsync();
            if(!stopped) Frame=await Task.Run(()=>ReadFrame(Frame.Revision+1));
            LastMessage=result?"CPU 已行动":"CPU 对局故障"; return result;
        }
        finally { Volatile.Write(ref busy,0); }
    }
    public void Stop() { stopped=true; inputHost.Dispose(); session.Dispose(); }
    public void Dispose() => Stop();
    public static Command Resolve(IReadOnlyList<ActionOffer> actions,InteractionIntent intent)=>InteractionResolver.Resolve(actions,intent);
}
