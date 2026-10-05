using BoardGame.Kernel;
namespace BoardGame.Runtime;

public sealed record DebugRequest(string Operation,long Revision=-1,Command? Command=null,LoadedModule? Module=null,string? Seat=null);
public sealed record DebugFrame(long Revision,SeatView View,List<ActionOffer> Actions,bool Cpu,SessionFault? Fault,string StateHash);
public sealed record DebugReply(DebugFrame Frame,string Message="",CommandResult? Result=null,SessionRecording? Recording=null);
public interface IDebugSession : IDisposable { Task<DebugReply> SendAsync(DebugRequest request); }

// A local author control surface; ordinary PlayerSession never exposes it to players or scripts.
public sealed class DebugSession : IDisposable
{
    private readonly PlaySession session;
    private readonly string observer;
    private long revision;
    public DebugSession(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,string observer)
    {
        if(!module.Initial.Seats.Any(s=>s.Id==observer)) throw new ArgumentException("Unknown observer seat");
        session=new PlaySession(module,seed,controllers); this.observer=observer;
    }
    public DebugReply Handle(DebugRequest request)
    {
        CommandResult? result=null; SessionRecording? recording=null; var message="";
        switch(request.Operation)
        {
            case "frame": break;
            case "submit":
                if(request.Revision!=revision) { message="stale-frame"; break; }
                result=session.SubmitHuman(observer,request.Command ?? new(){Kind="invalid"}); revision++; break;
            case "cpu":
                if(request.Revision!=revision) {message="stale-frame";break;}
                session.StepCpu(); revision++; break;
            case "reload": session.ReloadScripts(request.Module ?? throw new InvalidDataException("Missing script version")); revision++; message="脚本已切换，下条命令生效。"; break;
            case "recording": recording=session.ExportRecording(); break;
            default: throw new InvalidDataException("Unknown local debug operation: "+request.Operation);
        }
        var actions=session.Actions(); var view=session.View(observer);
        return new(new(revision,view,!session.IsCpu && session.CurrentSeat==observer?actions.ToList():[],session.IsCpu,session.Fault,session.AuthorityStateHash()),message,result,recording);
    }
    public void Dispose()=>session.Dispose();
}

public sealed class InProcessDebugSession : IDebugSession
{
    private readonly DebugSession session;
    private readonly SemaphoreSlim gate=new(1);
    private bool disposed;
    public InProcessDebugSession(LoadedModule module,uint seed,IReadOnlyDictionary<string,string> controllers,string observer) => session=new(module,seed,controllers,observer);
    public async Task<DebugReply> SendAsync(DebugRequest request)
    {
        await gate.WaitAsync();
        try { ObjectDisposedException.ThrowIf(disposed,this); return await Task.Run(()=>session.Handle(request)); }
        finally {gate.Release();}
    }
    public void Dispose() { disposed=true; session.Dispose(); }
}
