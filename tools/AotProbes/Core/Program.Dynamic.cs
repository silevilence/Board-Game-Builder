using System.Text.Json;
using CordiSharp;
using CordiSharp.Registry;
using CordiSharp.Logger;

var results = new List<(string Name, bool Passed, string Detail)>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
async Task Case(string name, Func<Task> run)
{
    try { await run(); results.Add((name, true, "")); }
    catch (Exception error) { results.Add((name, false, error.ToString())); }
}
await Case("class-plugin-reflection", async () =>
{
    var root = Context.Create();
    try
    {
        var handle = root.Plugin<ExamplePlugin, ExampleConfig>(new() { Amount = 3 });
        await handle.Await();
        Check(root.Get<int>("class-value") == 3, "Class injection failed");
        await handle.DisposeAsync();
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("duck-typed-apply", async () =>
{
    var root = Context.Create();
    try
    {
        var handle = root.Plugin(new { Name = "duck", Apply = (Func<Context, object?, object?>)((ctx, _) => { ctx.Provide("duck-value", 5); return null; }) });
        await handle.Await();
        Check(root.Get<int>("duck-value") == 5, "Duck apply failed");
        await handle.DisposeAsync();
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("typed-delegate-config", async () =>
{
    var root = Context.Create();
    try
    {
        var handle = root.Plugin<string>((ctx, value) => { ctx.Provide("typed-value", value); }, "seven");
        await handle.Await();
        Check(root.Get<string>("typed-value") == "seven", "Typed delegate config failed");
        await handle.DisposeAsync();
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("object-delegate-disposer", async () =>
{
    var root = Context.Create(); var disposed = 0;
    try
    {
        var handle = root.Plugin((Func<Context, object?, object?>)((ctx, _) => new Cleanup(() => disposed++)));
        await handle.Await(); await handle.DisposeAsync();
        Check(disposed == 1, "Object callback disposer changed");
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("typed-task-effect", async () =>
{
    var root = Context.Create(); var disposed = 0;
    try
    {
        var effect = root.Effect((Func<object?>)(() => Task.FromResult<IDisposable>(new Cleanup(() => disposed++))));
        await effect.AwaitDisposed();
        Check(disposed == 1, "Typed task result not disposed");
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("anonymous-logger-intercept", async () =>
{
    var root = Context.Create();
    try
    {
        var context = root.Intercept("logger", new { name = "dynamic-name", level = LogLevel.Debug });
        context.LoggerService.Get(context).Warn("message");
        Check(root.LoggerService.Buffer.Last().Name == "dynamic-name", "Anonymous logger config changed");
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
await Case("json-effect-label-escaping", async () =>
{
    var root = Context.Create();
    try
    {
        const string name = "quote\"\\中文\n";
        root.Provide(name, 1);
        Check(root.Fiber.GetEffects().Any(e => e.Label == "ctx.provide(" + JsonSerializer.Serialize(name) + ")"), "String JSON escaping changed");
    }
    finally { await root.Fiber.DisposePluginAsync(); }
});
var report = JsonSerializer.Serialize(new { success = results.All(r => r.Passed), checks = results.Select(r => new { name = r.Name, passed = r.Passed, detail = r.Detail }) }, new JsonSerializerOptions { WriteIndented = true });
if (args.Length == 1) File.WriteAllText(args[0], report);
Console.WriteLine(report);
return results.All(r => r.Passed) ? 0 : 1;

public sealed class ExampleConfig { public int Amount { get; set; } }
public sealed class ExamplePlugin : IPlugin<ExampleConfig>
{
    public void Load(Context context, ExampleConfig config) => context.Provide("class-value", config.Amount);
}
sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }
