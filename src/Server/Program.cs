using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

if (args.Length != 1) { Console.Error.WriteLine("Usage: Server <server.json>"); return 2; }
try
{
    var configPath = Path.GetFullPath(args[0]);
    var config = Wire.Read<ServerConfiguration>(File.ReadAllText(configPath));
    config.Validate();
    var directory = Path.GetDirectoryName(configPath)!;
    var module = GameRuntime.Load(Path.GetFullPath(config.Module, directory));
    using var room = new OnlineSession(module, config.Seed, config.Controllers, config.Options);
    var output = Path.GetFullPath(config.Output, directory);
    Directory.CreateDirectory(output);
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders(); // Never log authorization headers or request bodies.
    builder.WebHost.UseUrls(config.Listen);
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Limits.MaxRequestBodySize = 64 * 1024;
        k.Limits.MaxConcurrentConnections = 64;
        k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        if (config.Certificate != null)
            k.ConfigureHttpsDefaults(h => h.ServerCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                Path.GetFullPath(config.Certificate, directory), Environment.GetEnvironmentVariable("BGB_CERT_PASSWORD")));
    });
    var app = builder.Build();
    var auditGate = new object();
    void Audit(string code)
    {
        lock (auditGate) File.AppendAllText(Path.Combine(output, "events.jsonl"), Wire.Json(new { time = DateTimeOffset.UtcNow, code }) + "\n");
    }
    app.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var key = Encoding.UTF8.GetBytes(context.Request.Headers["X-Room-Key"].ToString());
            if (!CryptographicOperations.FixedTimeEquals(key, Encoding.UTF8.GetBytes(config.RoomKey)))
                throw new OnlineException("room-access-denied");
            await next(context).ConfigureAwait(false);
        }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var code = error is OnlineException ? error.Message : error is JsonException or BadHttpRequestException or InvalidDataException or ArgumentException ? "invalid-request" : "server-error";
            Audit(code);
            context.Response.StatusCode = error is OnlineException ? 409 : 400;
            await context.Response.WriteAsync(Wire.Json(new OnlineError(code))).ConfigureAwait(false);
        }
    });
    async Task<T> Body<T>(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        return Wire.Read<T>(text);
    }
    app.MapPost("/join", async (HttpContext context) =>
    {
        var request = await Body<JoinRequest>(context);
        var lease = request.Audience ? room.Watch(request.Identity) : room.Join(request.Identity, request.Seat);
        Audit(request.Audience ? "audience-joined" : "player-joined");
        return Results.Text(Wire.Json(lease), "application/json");
    });
    app.MapPost("/resume", async (HttpContext context) =>
    {
        var request = await Body<ResumeRequest>(context);
        var response = room.Resume(request.Identity, request.ResumeToken, request.AfterRevision);
        Audit("identity-resumed");
        return Results.Text(Wire.Json(response), "application/json");
    });
    app.MapPost("/rpc", async (HttpContext context) =>
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) throw new OnlineException("invalid-identity");
        var token = authorization[7..];
        var request = await Body<OnlineRequest>(context);
        OnlineReply reply;
        switch (request.Operation)
        {
            case OnlineOperations.Frame: reply = new(Frame: room.Read(token)); break;
            case OnlineOperations.CatchUp: reply = new(Frames: room.CatchUp(token, request.AfterRevision)); break;
            case OnlineOperations.Submit:
                var receipt = room.Submit(token, request.Proposal ?? throw new OnlineException("missing-proposal"));
                Audit(receipt.Result.Accepted ? "proposal-accepted" : receipt.Result.Reason);
                reply = new(Receipt: receipt); break;
            case OnlineOperations.Recording: reply = new(Recording: room.ExportPlayerRecording(token)); break;
            case OnlineOperations.Disconnect: room.Disconnect(token); reply = new(); break;
            case OnlineOperations.Leave: room.Leave(token); Audit("identity-left"); reply = new(); break;
            default: throw new OnlineException("unknown-operation");
        }
        return Results.Text(Wire.Json(reply), "application/json");
    });
    await app.StartAsync();
    Audit("server-started");
    File.WriteAllText(Path.Combine(output, "ready.json"), Wire.Json(room.Identity));
    Console.WriteLine($"READY {config.Listen} {module.Id} {room.Identity.Fingerprint}");
    void SaveAuthority()
    {
        File.WriteAllText(Path.Combine(output, "authority.json"), Wire.Json(room.ExportAuthorityRecording()));
        File.WriteAllText(Path.Combine(output, "diagnostics.json"), Wire.Json(room.ExportAuthorityDiagnostics()));
    }
    void SaveLifecycle()
    {
        lock (auditGate)
            foreach (var entry in room.DrainLifecycleEvents())
                File.AppendAllText(Path.Combine(output, "events.jsonl"), Wire.Json(entry) + "\n");
    }
    string? lastFault = null;
    bool terminalSaved = false;
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping))
        {
            if (File.Exists(Path.Combine(output, "stop.request"))) break;
            room.Maintain();
            room.StepCpu();
            SaveLifecycle();
            var status = room.Status;
            if (status.Fault != lastFault) { lastFault = status.Fault; Audit(status.Fault ?? "fault-cleared"); }
            if (!terminalSaved && (status.Ended || status.Closed || status.Fault != null)) { SaveAuthority(); terminalSaved = true; }
        }
    }
    catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
    finally
    {
        room.Close();
        SaveLifecycle();
        await Task.Delay(300);
        await app.StopAsync();
        SaveAuthority();
        Audit("server-stopped");
        await app.DisposeAsync();
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine($"server-start-or-run-failed: {error.Message}"); return 1; }

public sealed record ServerConfiguration(string Module, string Listen, string RoomKey, Dictionary<string, string> Controllers,
    uint Seed = 1, OnlineOptions? Options = null, string Output = "records", string? Certificate = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Module) || Controllers == null || string.IsNullOrWhiteSpace(Output) || string.IsNullOrWhiteSpace(RoomKey) || RoomKey.Length < 16)
            throw new ArgumentException("Module, controllers, output and a room key of at least 16 characters are required.");
        if (!Uri.TryCreate(Listen, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.AbsolutePath != "/")
            throw new ArgumentException("Listen must be an HTTP(S) root URL.");
        if (url.Scheme == "https" && Certificate == null) throw new ArgumentException("HTTPS requires a PKCS#12 certificate path.");
        if (url.Scheme == "http" && !url.IsLoopback) throw new ArgumentException("Remote listeners require HTTPS and a certificate.");
    }
}
