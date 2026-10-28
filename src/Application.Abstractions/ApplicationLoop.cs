namespace BoardGame.Runtime;

[Flags]
public enum HostMode { Headless=1, Graphical=2, All=Headless|Graphical }
public enum RuntimeScope { GraphicsHost, Instance, Viewport }
public enum ApplicationPhase { Update, Render }
public enum ApplicationStatus { Ready, Running, Stopping, Stopped, Faulted }

/// <summary>Ordering is independent of capability dependencies. IDs are local to a plugin;
/// edges use fully qualified "plugin/id" names in the same phase and scope.</summary>
public sealed record PhaseContribution(string Id,ApplicationPhase Phase,string[]? Before=null,string[]? After=null);
public sealed record PluginExecution(HostMode Modes=HostMode.All,RuntimeScope Scope=RuntimeScope.Instance,PhaseContribution[]? Contributions=null);
public sealed record ApplicationInput(long Sequence,string Kind,ReadOnlyMemory<byte> Data);
public sealed record UpdateFrame(long Tick,TimeSpan SimulationTime,TimeSpan Delta,IReadOnlyList<ApplicationInput> Inputs);
public sealed record RenderFrame(string Viewport,TimeSpan PresentationTime,TimeSpan Delta,long CompletedTick,double Interpolation);
public sealed record ApplicationFault(string Plugin,string Contribution,ApplicationPhase Phase,Exception Error);

/// <summary>Update results are prepared off the UI thread. Commit runs once after every update
/// in the tick succeeds and only while the instance is still active. Render receives published data.</summary>
public delegate ValueTask<Action?> ApplicationUpdate(UpdateFrame frame,CancellationToken cancellation);
public delegate void ApplicationRender(RenderFrame frame);

public sealed record ApplicationClockOptions(TimeSpan? FixedStep=null,int MaxCatchUpSteps=5,TimeSpan? MaxFrameDelta=null)
{
    public TimeSpan FrameLimit=>MaxFrameDelta ?? TimeSpan.FromMilliseconds(250);
    public void Validate()
    {
        if(FixedStep is {Ticks:<=0} || MaxCatchUpSteps is <1 or >1000 || FrameLimit<=TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ApplicationClockOptions));
    }
}

public interface IApplicationDriver : IAsyncDisposable
{
    ApplicationStatus Status {get;}
    ApplicationFault? Fault {get;}
    long CompletedTick {get;}
    TimeSpan SimulationTime {get;}
    TimeSpan DroppedTime {get;}
    bool Paused {get;set;}
    bool Enqueue(ApplicationInput input);
    Task AdvanceAsync(TimeSpan elapsed,CancellationToken cancellation=default);
    Task StepAsync(CancellationToken cancellation=default);
    Task StopAsync();
}
