namespace BoardGame.Editor;

// Gesture and button work belongs to the format fiber, including work suspended at an await.
public sealed class FlowEditorWork : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly CancellationToken token;
    private readonly DocumentWorkspace documents;
    private readonly Action<string> report;
    private readonly object gate = new();
    private readonly HashSet<Task> pending = [];
    private Task? disposal;

    public FlowEditorWork(EditorPluginScope scope, Action<string> report)
    {
        documents = scope.Service<DocumentWorkspace>("documents");
        this.report = report;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(scope.Cancellation);
        token = cancellation.Token;
        scope.Context.Effect(() => this);
    }

    public void Run(Func<CancellationToken, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposal != null || token.IsCancellationRequested) return;
            pending.Add(completion.Task);
        }
        var generation = documents.Generation;
        _ = Execute();
        async Task Execute()
        {
            try { token.ThrowIfCancellationRequested(); await action(token); }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && documents.Generation == generation) report(error.Message);
            }
            finally
            {
                lock (gate) { completion.TrySetResult(); pending.Remove(completion.Task); }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= Stop(pending.ToArray()));
    }
    private async Task Stop(Task[] tasks)
    {
        cancellation.Cancel();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        finally { cancellation.Dispose(); }
    }
}
