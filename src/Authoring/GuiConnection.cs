using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace BoardGame.Authoring;

public sealed record WorkspaceAdvertisement(string Instance, string Pipe, int Process, long ProcessStarted, string Workspace, long Generation, string? Project, int Api = 1);
public sealed record WorkspaceRequest(string Method, ProjectIdentity Identity, string RequestId, ProjectChanges? Changes = null, string? Intent = null, JsonElement? Arguments = null);
public sealed record WorkspaceResponse(bool Success, JsonElement Result, string? Code = null, string? Message = null);
public sealed record WorkspaceView(ProjectSource Source, Dictionary<string, byte[]> Files);

public static class WorkspaceConnections
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoardGameBuilder", "author", "connections");
    public static IReadOnlyList<WorkspaceAdvertisement> Discover()
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        var result = new List<WorkspaceAdvertisement>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<WorkspaceAdvertisement>(File.ReadAllText(path), AuthorProfiles.Json)!;
                using var process = Process.GetProcessById(item.Process);
                if (item.Api == 1 && !process.HasExited && process.StartTime.ToUniversalTime().Ticks == item.ProcessStarted) result.Add(item);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException) { }
        }
        return result;
    }
    internal static async Task<T> Receive<T>(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count is < 1 or > 256 * 1024 * 1024) throw new InvalidDataException("connection.size: message exceeds limit");
        var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonSerializer.Deserialize<T>(bytes, AuthorProfiles.Json) ?? throw new InvalidDataException("connection.message: null payload");
    }
    internal static async Task Send<T>(Stream stream, T value, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AuthorProfiles.Json);
        if (bytes.Length > 256 * 1024 * 1024) throw new InvalidDataException("connection.size: message exceeds limit");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
}

public sealed class WorkspaceConnectionServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Func<WorkspaceRequest, CancellationToken, Task<object>> handle;
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly string pipe = "bgb-author-" + Guid.NewGuid().ToString("N");
    private readonly string advertisement;
    private readonly Task loop;
    public string Instance => instance;
    public WorkspaceConnectionServer(Func<WorkspaceRequest, CancellationToken, Task<object>> handle)
    {
        this.handle = handle;
        Directory.CreateDirectory(WorkspaceConnections.DirectoryPath);
        advertisement = Path.Combine(WorkspaceConnections.DirectoryPath, instance + ".json");
        loop = Task.Run(Listen);
    }
    public void Publish(string workspace, long generation, string? project)
    {
        stop.Token.ThrowIfCancellationRequested();
        using var process = Process.GetCurrentProcess();
        var item = new WorkspaceAdvertisement(instance, pipe, process.Id, process.StartTime.ToUniversalTime().Ticks, workspace, generation, project);
        RunOperations.Write(advertisement, JsonSerializer.Serialize(item, AuthorProfiles.Json), true);
    }
    private async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var stream = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await stream.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var request = await WorkspaceConnections.Receive<WorkspaceRequest>(stream, timeout.Token).ConfigureAwait(false);
                WorkspaceResponse response;
                try
                {
                    var result = await handle(request, timeout.Token).ConfigureAwait(false);
                    response = new(true, JsonSerializer.SerializeToElement(result, AuthorProfiles.Json));
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { response = new(false, JsonSerializer.SerializeToElement<object?>(null), (error as AuthorOperationException)?.Code ?? "workspace.failure", error.Message); }
                await WorkspaceConnections.Send(stream, response, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidDataException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (File.Exists(advertisement)) File.Delete(advertisement);
        await loop.ConfigureAwait(false);
        stop.Dispose();
    }
}

public sealed class GuiEditMode : IProjectEditMode, IRecoverableEditMode, IWorkspaceCoordination, IDisposable
{
    private readonly WorkspaceAdvertisement connection;
    private readonly ProjectIdentity identity;
    private readonly CancellationTokenSource lifetime = new();
    public GuiEditMode(string project, string profile, string? instance)
    {
        var matches = WorkspaceConnections.Discover().Where(c => string.Equals(c.Project, project, StringComparison.OrdinalIgnoreCase) && (instance == null || c.Instance == instance)).ToArray();
        if (matches.Length != 1) throw new AuthorOperationException("connection.selection", matches.Length == 0 ? "No matching Editor connection; enable author.connection. Disk mode is not a fallback." : "Multiple matching Editor instances; select one with --instance");
        connection = matches[0]; identity = new(project, profile, "gui", connection.Workspace, connection.Generation);
    }
    private async Task<T> Request<T>(string method, string request, CancellationToken cancellation, ProjectChanges? changes = null, string? intent = null, JsonElement? arguments = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var client = new NamedPipeClientStream(".", connection.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(5000, timeout.Token);
            await WorkspaceConnections.Send(client, new WorkspaceRequest(method, identity, request, changes, intent, arguments), timeout.Token);
            var reply = await WorkspaceConnections.Receive<WorkspaceResponse>(client, timeout.Token);
            if (!reply.Success) throw new AuthorOperationException(reply.Code!, reply.Message!);
            return reply.Result.Deserialize<T>(AuthorProfiles.Json)!;
        }
        catch (IOException error) { throw new AuthorOperationException("connection.lost", "Connection lost; a write may have applied. Query its request ID before retrying. " + error.Message); }
    }
    public async Task<ProjectView> Read(CancellationToken cancellation = default)
    {
        var value = await Request<WorkspaceView>("read", "read", cancellation);
        var view = new ProjectView(value.Source.Identity, value.Files);
        if (view.Source != value.Source || view.Source.Identity != identity) throw new AuthorOperationException("connection.version", "Workspace response has a different identity or version");
        return view;
    }
    public Task<EditReceipt> Apply(ProjectChanges changes, CancellationToken cancellation = default) => Request<EditReceipt>("apply", changes.RequestId, cancellation, changes);
    public Task<EditReceipt> Result(string requestId, CancellationToken cancellation = default) => Request<EditReceipt>("result", requestId, cancellation);
    public Task<EditReceipt?> Find(string requestId, string intent, CancellationToken cancellation = default) => Request<EditReceipt?>("find", requestId, cancellation, intent: intent);
    public Task<EditReceipt> Recover(string requestId, CancellationToken cancellation = default) => Result(requestId, cancellation);
    public Task<EditReceipt> Save(string requestId, CancellationToken cancellation = default) => Request<EditReceipt>("save", requestId, cancellation);
    public Task<ExternalStatus> ExternalStatus(CancellationToken cancellation = default) => Request<ExternalStatus>("external.status", "status", cancellation);
    public Task<EditReceipt> ResolveExternal(string requestId, JsonElement arguments, CancellationToken cancellation = default) => Request<EditReceipt>("external.resolve", requestId, cancellation, arguments: arguments);
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    public static AuthorPlugin Plugin => new("edit.gui", "1", 1, ["author.mode"], [], scope =>
        scope.Provide("author.mode", scope.Own(new GuiEditMode(scope.Profile.Project, scope.Profile.Name, scope.Profile.Profile.Workspace))), "Connect to one explicit Editor workspace; no disk fallback");
}
