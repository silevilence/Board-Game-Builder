using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

public sealed partial class PlaySession
{
    private readonly SemaphoreSlim driveGate=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly object stopGate=new();
    private Task? stopped;
    private static readonly TimeSpan BoardStep=TimeSpan.FromTicks(TimeSpan.TicksPerSecond/60);
    public async Task<CommandResult> SubmitHumanAsync(string seat,Command command,CancellationToken cancellation=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(new[]{lifetime.Token,sessionCancellation,cancellation});
        await driveGate.WaitAsync(linked.Token).ConfigureAwait(false);
        long? sequence=null;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var request=new BoardUpdateRequest(session.ExportAuthorityLog().EntryCount,seat,command);
            sequence=Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(request,Wire.Options));
            await AdvanceBoundary(linked.Token).ConfigureAwait(false);
            var result=UpdateResults.SingleOrDefault(r=>r.Sequence==sequence);
            return result==null?new(0,false,"superseded"):new(result.CommandSequence,result.Accepted,result.Reason);
        }
        finally
        {
            if(sequence is {} queued) Driver.WithdrawInput(queued);
            driveGate.Release();
        }
    }
    public async Task<bool> StepCpuAsync(CancellationToken cancellation=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(new[]{lifetime.Token,sessionCancellation,cancellation});
        await driveGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if(session.Ended || session.Fault!=null || !session.IsCpu) return false;
            var before=session.Steps;
            await AdvanceBoundary(linked.Token).ConfigureAwait(false);
            return session.Steps>before;
        }
        finally {driveGate.Release();}
    }
    public async Task AdvanceAsync(TimeSpan elapsed,CancellationToken cancellation=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(new[]{lifetime.Token,sessionCancellation,cancellation});
        await driveGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try {await Driver.AdvanceAsync(elapsed,linked.Token).ConfigureAwait(false);}
        finally {driveGate.Release();}
    }
    private Task AdvanceBoundary(CancellationToken cancellation)=>Driver.Paused?Driver.StepAsync(cancellation):Driver.AdvanceAsync(BoardStep,cancellation);
    public ValueTask DisposeAsync()
    {
        lock(stopGate) return new(stopped ??= StopCore());
    }
    private async Task StopCore()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);session.CancelFlow();
        try {await host.DisposeAsync().ConfigureAwait(false);}
        finally {lifetime.Dispose();}
    }
}
