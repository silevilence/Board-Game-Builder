using System.Diagnostics;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

/// <summary>One wall-clock pump per instance. Viewports never start their own simulation.</summary>
public sealed class ApplicationPump : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim updates=new(1,1);
    private readonly IApplicationDriver driver;
    private Task? stopped;
    public Task Completion {get;}
    public ApplicationPump(IApplicationDriver driver,TimeSpan? interval=null)
    {
        this.driver=driver;
        var cadence=interval ?? TimeSpan.FromMilliseconds(16);
        if(cadence<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(interval));
        Completion=Task.Run(async ()=>
        {
            using var timer=new PeriodicTimer(cadence);var clock=Stopwatch.StartNew();var previous=TimeSpan.Zero;
            try
            {
                while(await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                {
                    var now=clock.Elapsed;var elapsed=now-previous;previous=now;
                    await updates.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    try {if(!driver.Paused)await driver.AdvanceAsync(elapsed,lifetime.Token).ConfigureAwait(false);}
                    finally {updates.Release();}
                    if(driver.Status is ApplicationStatus.Stopped or ApplicationStatus.Faulted)break;
                }
            }
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        });
    }
    public async Task SetPausedAsync(bool paused)
    {
        driver.Paused=paused;
        await updates.WaitAsync(lifetime.Token).ConfigureAwait(false);updates.Release();
    }
    public async Task StepAsync(CancellationToken cancellation=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellation);
        await updates.WaitAsync(linked.Token).ConfigureAwait(false);
        try {await driver.StepAsync(linked.Token).ConfigureAwait(false);}
        finally {updates.Release();}
    }
    public ValueTask DisposeAsync(){lock(lifetime)return new(stopped ??=Stop());}
    private async Task Stop()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        try{await Completion.ConfigureAwait(false);}
        finally{await updates.WaitAsync().ConfigureAwait(false);updates.Dispose();lifetime.Dispose();}
    }
}
