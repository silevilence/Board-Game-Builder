using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;

var output = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "core-result.json");
var workspace = Path.Combine(Path.GetDirectoryName(output)!, "core-input-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
File.WriteAllText(Path.Combine(workspace, "data.json"), "{\"amount\":2}");
const string entry = """
{"format":1,"id":"core-aot-probe","version":"1","plugins":[{"id":"data","version":"1","api":1,"configuration":{"path":"data.json"}},{"id":"accumulator","version":"1","api":1,"configuration":{}}]}
""";
File.WriteAllText(Path.Combine(workspace, GameRuntime.Entry), entry);
var results = new List<(string Name, bool Passed, string Detail)>();
var fingerprint = "";
var resourceCount = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Reject(Action operation, string reason)
{
    try { operation(); }
    catch (Exception error) when (error.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException("Expected rejection containing: " + reason);
}
async Task Case(string name, Func<Task> test)
{
    try { await test(); results.Add((name, true, "")); }
    catch (Exception error) { results.Add((name, false, error.ToString())); }
}
JsonElement Json(string value) { using var json = JsonDocument.Parse(value); return json.RootElement.Clone(); }
RuntimePluginSelection Select(string id) => new(id, "1", 1, Json("{}"));
var loader = new ApprovedApplicationPlugin(_ => new("data", "1", 1, ["definition"], [], scope =>
    scope.Provide("definition", new Loader(scope.Configuration.GetProperty("path").GetString()!)))
{
    Content = new(["path"], RequiredCapabilities: ["value"], Prepare: true,
        Definition: c => new(c.GetProperty("path").GetString()!, new Dictionary<string, string>()))
});
var accumulator = new ApprovedApplicationPlugin(content => new("accumulator", "1", 1, ["value"], [], scope =>
{
    var value = new Counter();
    scope.Provide("value", value);
    scope.Own(new Cleanup(() => Interlocked.Increment(ref resourceCount)));
    var amount = content!.Payload.Data.GetProperty("amount").GetInt32();
    scope.Update("advance", (frame, _) => ValueTask.FromResult<Action?>(() => { value.Amount += amount; value.Inputs += frame.Inputs.Count; }));
}) { Execution = new(Contributions: [new("advance", ApplicationPhase.Update)]) });
var catalog = new ApplicationCatalog([loader, accumulator]);

await Case("content-package-snapshot-restore-update", async () =>
{
    var content = GameRuntime.Load(workspace, catalog.Content);
    fingerprint = content.Fingerprint;
    var package = Path.Combine(Path.GetDirectoryName(output)!, "core-" + Guid.NewGuid().ToString("N") + ".bgm");
    GamePackage.Pack(workspace, package, catalog.Content);
    var packed = GameRuntime.Load(package, catalog.Content);
    var snapshotFile = package + ".snapshot.json";
    packed.SaveSnapshot(snapshotFile);
    var saved = GameRuntime.Load(snapshotFile, catalog.Content);
    var restored = GameRuntime.Restore(saved.ExportSnapshot(), catalog.Content);
    Check(new[] { packed, saved, restored }.All(c => c.Fingerprint == fingerprint), "Content identity changed");
    var state = new List<int>();
    foreach (var input in new[] { content, packed, saved, restored })
    {
        await using var host = await catalog.StartAsync(input);
        host.Driver.Paused = true;
        host.Driver.Enqueue("once", new byte[] { 1 });
        for (var i = 0; i < 3; i++) await host.Driver.StepAsync();
        var value = host.Service<Counter>("value");
        Check(value.Amount == 6 && value.Inputs == 1 && host.Driver.CompletedTick == 3, "Update/input semantics changed");
        state.Add(value.Amount);
    }
    Check(resourceCount == 4 && state.Distinct().Count() == 1, "Session cleanup/state differ");
});
await Case("snapshot-content-tamper-rejected", () =>
{
    var content = GameRuntime.Load(workspace, catalog.Content);
    var snapshot = content.ExportSnapshot();
    snapshot.Files["data.json"] = Encoding.UTF8.GetBytes("{\"amount\":9}");
    Reject(() => GameRuntime.Restore(snapshot, catalog.Content), "identity mismatch");
    Check(content.Payload.Data.GetProperty("amount").GetInt32() == 2, "Export mutated live content");
    return Task.CompletedTask;
});
await Case("duplicate-and-unknown-json-rejected", () =>
{
    foreach (var (bad, expected) in new[]
    {
        (entry.Replace("\"format\":1", "\"format\":1,\"format\":1", StringComparison.Ordinal), "duplicate"),
        (entry.Replace("\"format\":1", "\"format\":1,\"unknown\":1", StringComparison.Ordinal), "unknown")
    })
    {
        var files = new Dictionary<string, byte[]> { [GameRuntime.Entry] = Encoding.UTF8.GetBytes(bad), ["data.json"] = Encoding.UTF8.GetBytes("{\"amount\":2}") };
        Reject(() => GameRuntime.LoadFiles(files, catalog.Content), expected);
    }
    return Task.CompletedTask;
});
await Case("catalog-identity-revalidation", async () =>
{
    var content = GameRuntime.Load(workspace, catalog.Content);
    var restricted = new ApplicationCatalog([loader]);
    try { await using var host = await restricted.StartAsync(content); }
    catch (InvalidDataException error) when (error.Message.Contains("unknown or unapproved", StringComparison.Ordinal)) { return; }
    throw new InvalidOperationException("Broader catalog content accepted");
});
await Case("activation-failure-cleanup", async () =>
{
    var disposed = 0;
    var failure = new RuntimePluginDefinition("failure", "1", 1, ["failure"], [], scope =>
    {
        scope.Own(new Cleanup(() => Interlocked.Increment(ref disposed)));
        throw new InvalidOperationException("expected activation failure");
    });
    try { await using var host = await RuntimeHost.StartAsync(new(1, "failure-probe", "1", [Select("failure")]), [failure], []); }
    catch (InvalidDataException error) when (error.Message.Contains("activation failed", StringComparison.Ordinal))
    { Check(disposed == 1, "Failed activation leaked resource"); return; }
    throw new InvalidOperationException("Activation unexpectedly succeeded");
});
await Case("close-cancels-and-awaits-update", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finished = false;
    var disposed = false;
    var published = false;
    var plugin = new RuntimePluginDefinition("blocking", "1", 1, [], [], scope =>
    {
        scope.Own(new Cleanup(() => { Check(finished, "Released before update finished"); disposed = true; }));
        scope.Update("wait", async (_, cancellation) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
            finally { finished = true; }
            return () => published = true;
        });
    }) { Execution = new(Contributions: [new("wait", ApplicationPhase.Update)]) };
    var host = await RuntimeHost.StartAsync(new(1, "cancellation-probe", "1", [Select("blocking")]), [plugin], []);
    host.Driver.Paused = true;
    var pending = host.Driver.StepAsync();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    try { await pending; } catch (OperationCanceledException) { }
    Check(finished && disposed && !published, "Close published or leaked work");
    await host.DisposeAsync();
});

using var buffer = new MemoryStream();
using (var writer = new Utf8JsonWriter(buffer, new() { Indented = true }))
{
    writer.WriteStartObject(); writer.WriteBoolean("success", results.All(r => r.Passed));
    writer.WriteString("fingerprint", fingerprint); writer.WriteBoolean("dynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
    writer.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    writer.WriteStartArray("checks");
    foreach (var result in results) { writer.WriteStartObject(); writer.WriteString("name", result.Name); writer.WriteBoolean("passed", result.Passed); writer.WriteString("detail", result.Detail); writer.WriteEndObject(); }
    writer.WriteEndArray(); writer.WriteStartArray("nativeModules");
    using var process = Process.GetCurrentProcess();
    foreach (ProcessModule module in process.Modules) writer.WriteStringValue(module.FileName);
    writer.WriteEndArray(); writer.WriteEndObject();
}
var report = Encoding.UTF8.GetString(buffer.ToArray());
File.WriteAllText(output, report); Console.WriteLine(report);
return results.All(r => r.Passed) ? 0 : 1;

sealed class Counter { public int Amount; public int Inputs; }
sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }
sealed class Loader(string path) : IContentLoader
{
    public string PayloadFormat => "probe.counter";
    public int PayloadVersion => 1;
    public JsonElement Load(IContentSource source) { using var json = JsonDocument.Parse(source.Read(path)); return json.RootElement.Clone(); }
}
