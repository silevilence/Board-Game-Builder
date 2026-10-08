using System.Net.Http.Headers;
using System.Text;
using BoardGame.Kernel;

namespace BoardGame.Networking;

public static class OnlineProtocol
{
    public const int Version = 1;
    public const int RecordingFormat = 1;
}
public static class OnlineOperations
{
    public const string Frame = "frame";
    public const string CatchUp = "catch-up";
    public const string Submit = "submit";
    public const string Recording = "recording";
    public const string Disconnect = "disconnect";
    public const string Leave = "leave";
}

public sealed record JoinRequest(OnlineIdentity Identity, string? Seat = null, bool Audience = false);
public sealed record ResumeRequest(OnlineIdentity Identity, string ResumeToken, long AfterRevision = -1);
public sealed record OnlineRequest(string Operation, long AfterRevision = -1, OnlineProposal? Proposal = null);
public sealed record OnlineReply(OnlineFrame? Frame = null, OnlineReceipt? Receipt = null,
    IReadOnlyList<OnlineFrame>? Frames = null, OnlineRecording? Recording = null);
public sealed record OnlineError(string Code);

// A transport client holds no authoritative game, scripts, random state or other seats' data.
public sealed class OnlineClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string roomKey;
    public OnlineClient(Uri server, string roomKey, HttpMessageHandler? handler = null)
    {
        if (server.Scheme is not ("http" or "https")) throw new ArgumentException("Expected an HTTP(S) server URL.");
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = server;
        http.Timeout = TimeSpan.FromSeconds(15);
        this.roomKey = roomKey;
    }
    public Task<SeatLease> JoinAsync(JoinRequest request, CancellationToken cancellation = default) =>
        Send<SeatLease>("join", request, null, cancellation);
    public Task<OnlineResume> ResumeAsync(ResumeRequest request, CancellationToken cancellation = default) =>
        Send<OnlineResume>("resume", request, null, cancellation);
    public Task<OnlineReply> SendAsync(string token, OnlineRequest request, CancellationToken cancellation = default) =>
        Send<OnlineReply>("rpc", request, token, cancellation);
    private async Task<T> Send<T>(string path, object value, string? token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Room-Key", roomKey);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(Wire.Json(value), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                OnlineError? error;
                try { error = Wire.Read<OnlineError>(body); } catch { error = null; }
                throw new OnlineException(error?.Code ?? "server-error");
            }
            return Wire.Read<T>(body);
        }
        catch (HttpRequestException) { throw new OnlineException("server-unreachable"); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new OnlineException("server-timeout"); }
    }
    public void Dispose() => http.Dispose();
}
