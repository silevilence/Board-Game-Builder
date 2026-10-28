using System.Globalization;
using BoardGame.Kernel;
namespace BoardGame.Runtime;
public sealed partial class PlaySession : IGameSession,IAsyncDisposable
{
    private readonly RuntimeHost host;
    private readonly IGameSession session;
    private readonly CancellationToken sessionCancellation;
    public PlaySession(LoadedModule module,uint seed=1,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500,CancellationToken cancellation=default)
    {
        sessionCancellation=cancellation;
        host=RuntimeComposition.Start(module,seed,controllers,maxSteps,cancellation);
        session=host.Service<IGameSession>("session");
    }
    private PlaySession(RuntimeHost host,CancellationToken cancellation) {this.host=host;sessionCancellation=cancellation;session=host.Service<IGameSession>("session");}
    public static PlaySession Restore(LoadedModule module,SessionRecording recording,IReadOnlyDictionary<string,string>? controllers=null,int maxSteps=500,CancellationToken cancellation=default)=>
        new(RuntimeComposition.Start(module,recording.Log.Seed,controllers,maxSteps,cancellation,Wire.Copy(recording)),cancellation);
    public SessionFault? Fault=>session.Fault;
    public IApplicationDriver Driver=>host.Driver!;
    public IReadOnlyList<BoardUpdateResult> UpdateResults=>((IBoardUpdateState)session).UpdateResults;
    public int Steps=>session.Steps;
    public bool Ended=>session.Ended;
    public string CurrentSeat=>session.CurrentSeat;
    public bool IsCpu=>session.IsCpu;
    public bool SupportsCpu=>session.SupportsCpu;
    public SeatView View(string seat)=>session.View(seat);
    public IReadOnlyList<ActionOffer> Actions(string? seat=null)=>session.Actions(seat);
    public CommandResult SubmitHuman(string seat,Command command)=>SubmitHumanAsync(seat,command).GetAwaiter().GetResult();
    public bool StepCpu()=>StepCpuAsync().GetAwaiter().GetResult();
    public void CancelFlow()=>session.CancelFlow();
    public void ResumeFlow(CancellationToken cancellation=default)=>session.ResumeFlow(cancellation);
    public void SetController(string seat,string controller)=>session.SetController(seat,controller);
    public ReplayLog ExportAuthorityLog()=>session.ExportAuthorityLog();
    public SessionRecording ExportRecording()=>session.ExportRecording();
    public IReadOnlyList<Diagnostic> ExportDiagnostics()=>session.ExportDiagnostics();
    public GameState ExportAuthorityState()=>session.ExportAuthorityState();
    public string AuthorityStateHash()=>session.AuthorityStateHash();
    public void ReloadScripts(LoadedModule candidate)=>session.ReloadScripts(candidate);
    public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();
}
