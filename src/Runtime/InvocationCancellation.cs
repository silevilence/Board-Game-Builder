namespace BoardGame.Runtime;

internal interface ICancellableExecution
{
    IDisposable Enter(CancellationToken token);
}
internal sealed class InvocationCancellation : ICancellableExecution
{
    private readonly AsyncLocal<CancellationToken> current=new();
    public CancellationToken Token=>current.Value;
    public IDisposable Enter(CancellationToken token)
    {
        var previous=current.Value;current.Value=token;
        return new Restore(()=>current.Value=previous);
    }
    private sealed class Restore(Action restore) : IDisposable
    {public void Dispose()=>restore();}
}
