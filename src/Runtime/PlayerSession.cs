using BoardGame.Kernel;
namespace BoardGame.Runtime;

public sealed record InteractionIntent(string Kind,string? Entity=null,string? Target=null,double X=0,double Y=0,string? Action=null);
public sealed record SessionFrame(long Revision,SeatView View,IReadOnlyList<ActionOffer> Actions,bool Cpu,SessionFault? Fault);

// One owner for each PlaySession. UI code keeps the last immutable projection during worker execution.
public sealed class PlayerSession : IDisposable
{
    private readonly PlaySession session;
    private readonly string observer;
    private int busy;
    private volatile bool stopped;
    public bool Busy => Volatile.Read(ref busy)!=0;
    public SessionFrame Frame { get; private set; }
    public string LastMessage { get; private set; }="";
    private PlayerSession(PlaySession session,string observer)
    {
        this.session=session; this.observer=observer; Frame=ReadFrame(0);
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
    public async Task<CommandResult> SubmitAsync(long revision,InteractionIntent intent)
    {
        if(Interlocked.CompareExchange(ref busy,1,0)!=0) return new(0,false,"busy");
        try
        {
            if(stopped || revision!=Frame.Revision || Frame.Cpu || Frame.View.Ended || Frame.Fault!=null) return new(0,false,"stale-or-inactive");
            var command=Resolve(Frame.Actions,intent);
            var result=await session.SubmitHumanAsync(observer,command);
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
    public void Stop() { stopped=true; session.Dispose(); }
    public void Dispose() => Stop();
    public static Command Resolve(IReadOnlyList<ActionOffer> actions,InteractionIntent intent)
    {
        if(intent.Kind=="action") return actions.FirstOrDefault(a=>a.Id==intent.Action)?.Command ?? new() { Kind="invalid-action" };
        if(intent.Kind is "shortcut" or "double-click")
            return actions.FirstOrDefault(a=>a.Shortcuts?.Contains(intent.Kind=="double-click"?"double-click":intent.Action ?? "")==true
                && (a.Command.Entity==null || a.Command.Entity==intent.Entity))?.Command ?? new() {Kind="invalid-shortcut"};
        if(intent.Kind=="click") intent=intent with {Kind="drag"};
        var offer=actions.FirstOrDefault(a=>a.Gesture==intent.Kind && a.Command.Entity==intent.Entity && a.Target==intent.Target);
        if(offer!=null) return offer.Command;
        return intent.Kind switch
        {
            "drag" => intent.Target==null ? new() { Kind="move",Entity=intent.Entity,X=intent.X,Y=intent.Y } : new() { Kind="transfer",Entity=intent.Entity,Target=intent.Target },
            "flip" => new() { Kind="flip",Entity=intent.Entity },
            "draw" => new() { Kind="draw",Entity=intent.Entity,Target=intent.Target },
            _ => new() { Kind="invalid-intent" }
        };
    }
}
