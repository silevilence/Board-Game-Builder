namespace BoardGame.Runtime;

// One owner per runtime instance. CordiSharp still owns the registered callbacks/resources.
internal sealed class ApplicationDriver : IApplicationDriver
{
    private readonly (ScheduledContribution Phase,ApplicationUpdate Callback)[] updates;
    private readonly ApplicationClockOptions options;
    private readonly object gate=new();
    private readonly CancellationTokenSource stop=new();
    private readonly Queue<ApplicationInput> inputs=[];
    private ApplicationStatus status=ApplicationStatus.Ready;
    private ApplicationFault? fault;
    private Task? running;
    private Task? stopping;
    private bool busy,paused,publicationValid=true,disposed;
    private long tick,sequence=-1;
    private int queuedBytes;
    private TimeSpan simulation,accumulator,dropped;
    internal ApplicationDriver((ScheduledContribution,ApplicationUpdate)[] updates,ApplicationClockOptions options)
    {this.updates=updates;this.options=options;}
    public ApplicationStatus Status {get {lock(gate) return status;}}
    public ApplicationFault? Fault {get {lock(gate) return fault;}}
    public long CompletedTick {get {lock(gate) return tick;}}
    public TimeSpan SimulationTime {get {lock(gate) return simulation;}}
    public TimeSpan DroppedTime {get {lock(gate) return dropped;}}
    public bool Paused
    {
        get {lock(gate) return paused;}
        set {lock(gate) {EnsureActive();paused=value;accumulator=TimeSpan.Zero;}}
    }
    public bool Enqueue(ApplicationInput input)
    {
        lock(gate)
        {
            EnsureActive();
            if(input.Sequence<=sequence) return false;
            if(string.IsNullOrWhiteSpace(input.Kind) || input.Data.Length>1024*1024 || inputs.Count>=4096 || queuedBytes+input.Data.Length>4*1024*1024)
                throw new ArgumentException("Invalid input or input budget exceeded",nameof(input));
            inputs.Enqueue(input with {Data=input.Data.ToArray()});sequence=input.Sequence;queuedBytes+=input.Data.Length;
            return true;
        }
    }
    public Task AdvanceAsync(TimeSpan elapsed,CancellationToken cancellation=default)=>Start(elapsed,false,cancellation);
    public Task StepAsync(CancellationToken cancellation=default)=>Start(options.FixedStep ?? TimeSpan.FromTicks(TimeSpan.TicksPerSecond/60),true,cancellation);
    private Task Start(TimeSpan elapsed,bool step,CancellationToken cancellation)
    {
        if(elapsed<TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        lock(gate)
        {
            EnsureActive();
            if(busy) throw new InvalidOperationException("An instance already has an update in progress");
            if(step && !paused) throw new InvalidOperationException("Single step requires a paused instance");
            cancellation.ThrowIfCancellationRequested();
            busy=true;status=ApplicationStatus.Running;
            return running=Task.Run(async ()=>
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(stop.Token,cancellation);
                try {await Advance(elapsed,step,linked.Token).ConfigureAwait(false);}
                finally {lock(gate) busy=false;}
            });
        }
    }
    private async Task Advance(TimeSpan elapsed,bool step,CancellationToken cancellation)
    {
        if(step) {await Tick(elapsed,cancellation).ConfigureAwait(false);return;}
        lock(gate)
        {
            if(paused) {accumulator=TimeSpan.Zero;return;}
            if(elapsed>options.FrameLimit) {dropped+=elapsed-options.FrameLimit;elapsed=options.FrameLimit;}
            if(options.FixedStep!=null) accumulator+=elapsed;
        }
        if(options.FixedStep is not {} fixedStep)
        {
            if(elapsed>TimeSpan.Zero) await Tick(elapsed,cancellation).ConfigureAwait(false);
            return;
        }
        for(var count=0;count<options.MaxCatchUpSteps;count++)
        {
            lock(gate)
            {
                if(paused || accumulator<fixedStep) return;
                accumulator-=fixedStep;
            }
            await Tick(fixedStep,cancellation).ConfigureAwait(false);
        }
        lock(gate)
        {
            var excess=accumulator.Ticks/fixedStep.Ticks*fixedStep.Ticks;
            accumulator-=TimeSpan.FromTicks(excess);dropped+=TimeSpan.FromTicks(excess);
        }
    }
    private async Task Tick(TimeSpan delta,CancellationToken cancellation)
    {
        UpdateFrame frame;
        lock(gate)
        {
            cancellation.ThrowIfCancellationRequested();EnsureActive();
            frame=new(tick+1,simulation+delta,delta,Array.AsReadOnly(inputs.ToArray()));
            inputs.Clear();queuedBytes=0;
        }
        List<(ScheduledContribution Phase,Action Commit)> pending=[];
        foreach(var (phase,callback) in updates)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var commit=await callback(frame,cancellation).ConfigureAwait(false);
                if(commit!=null) pending.Add((phase,commit));
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) {throw;}
            catch(Exception error) {Fail(phase,error);throw new InvalidOperationException(phase.Key+": update failed",error);}
        }
        lock(gate)
        {
            cancellation.ThrowIfCancellationRequested();EnsureActive();
            foreach(var (phase,commit) in pending)
                try {cancellation.ThrowIfCancellationRequested();EnsureActive();commit();}
                catch(Exception error)
                {
                    publicationValid=false;Fail(phase,error);
                    throw new InvalidOperationException(phase.Key+": commit failed; presentation disabled",error);
                }
            tick=frame.Tick;simulation=frame.SimulationTime;
        }
    }
    private void Fail(ScheduledContribution phase,Exception error)
    {lock(gate) {fault=new(phase.Plugin,phase.Declaration.Id,ApplicationPhase.Update,error);status=ApplicationStatus.Faulted;}}
    private void EnsureActive()
    {
        if(status is ApplicationStatus.Stopping or ApplicationStatus.Stopped or ApplicationStatus.Faulted)
            throw new InvalidOperationException("Application is "+status);
    }
    internal void Present(Action<long,double> render)
    {
        lock(gate)
        {
            if(stopping!=null || !publicationValid || status is ApplicationStatus.Stopping or ApplicationStatus.Stopped)
                throw new InvalidOperationException("Application presentation unavailable");
            render(tick,options.FixedStep is {} step?Math.Clamp((double)accumulator.Ticks/step.Ticks,0,1):0);
        }
    }
    public Task StopAsync()
    {
        lock(gate)
        {
            if(stopping!=null) return stopping;
            if(status!=ApplicationStatus.Faulted) status=ApplicationStatus.Stopping;
            inputs.Clear();queuedBytes=0;
            var pending=running;
            return stopping=Task.Run(async ()=>
            {
                try {await stop.CancelAsync().ConfigureAwait(false);}
                finally
                {
                    try {if(pending!=null) await pending.ConfigureAwait(false);}
                    catch { /* Fault is retained; stopping must still release all resources. */ }
                    lock(gate) {if(status!=ApplicationStatus.Faulted) status=ApplicationStatus.Stopped;}
                }
            });
        }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock(gate) {if(!disposed) {stop.Dispose();disposed=true;}}
    }
}
