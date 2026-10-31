using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BoardGame.Wasm;
using Wasmtime;

var results = new List<(string Name, bool Passed, string Detail)>();
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Case(string name, Action test)
{
    try { test(); results.Add((name, true, "")); }
    catch (Exception error) { results.Add((name, false, error.ToString())); }
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException("Expected rejection containing: " + message);
}
byte[] Code(string text) => Module.ConvertText(text);
var sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "counter.wasm"));
Case("sample-fixed-signatures", () =>
{
    using var executor = new WasmExecutor([new("counter", sample)]);
    Check(executor.Invoke("counter", c => c.ReadInt("bgb_api")) == 1, "API mismatch");
    Check(executor.Invoke("counter", c => c.ReadInt("bgb_version")) == 1, "Version mismatch");
    Check(executor.Invoke("counter", c => c.ReadInt("bgb_init")) == 3, "Capabilities mismatch");
    var value = 0;
    for (var i = 0; i < 3; i++) value = executor.Invoke("counter", c => c.ApplyInt("bgb_command", value, 1));
    Check(value == 3, "Counter result differs");
});
Case("fresh-store-and-call-release", () =>
{
    using var executor = new WasmExecutor([new("state", Code("(module (global $x (mut i32) (i32.const 0)) (func (export \"count\") (result i32) global.get $x i32.const 1 i32.add global.set $x global.get $x))"))]);
    WasmCall? saved = null;
    for (var i = 0; i < 3; i++) Check(executor.Invoke("state", c => { saved = c; return c.ReadInt("count"); }) == 1, "State escaped invocation");
    Reject(() => saved!.ReadInt("count"), "disposed");
    executor.Dispose(); executor.Dispose();
    Reject(() => executor.Invoke("state", c => 0), "disposed");
});
foreach (var import in new[] { "host", "wasi_snapshot_preview1" })
    Case("reject-import-" + import, () => Reject(() => { using var executor = new WasmExecutor([new("import", Code("(module (import \"" + import + "\" \"state\" (func)))"))]); }, "imports"));
Case("reject-invalid-bytes", () => Reject(() => { using var executor = new WasmExecutor([new("invalid", new byte[] { 0, 1, 2 })]); }, "not valid"));
Case("reject-size-budget", () => Reject(() => { using var executor = new WasmExecutor([new("sample", sample)], new(MaxModuleBytes: 1)); }, "budget"));
Case("reject-duplicate", () => Reject(() => { using var executor = new WasmExecutor([new("sample", sample), new("sample", sample)]); }, "duplicate"));
Case("fuel-budget", () =>
{
    using var executor = new WasmExecutor([new("loop", Code("(module (func (export \"run\") (result i32) (loop $forever br $forever) i32.const 0))"))]);
    Reject(() => executor.Invoke("loop", c => c.ReadInt("run")), "fuel");
});
Case("memory-budget", () =>
{
    using var executor = new WasmExecutor([new("memory", Code("(module (memory 65) (func (export \"run\") (result i32) i32.const 0))"))]);
    Reject(() => executor.Invoke("memory", c => c.ReadInt("run")), "memory");
});
Case("memory-grow-budget", () =>
{
    using var executor = new WasmExecutor([new("memory", Code("(module (memory 1) (func (export \"run\") (result i32) i32.const 100 memory.grow))"))]);
    Check(executor.Invoke("memory", c => c.ReadInt("run")) == -1, "Memory growth escaped limit");
});
foreach (var (body, expected) in new[]
{
    ("(memory 1) (func (export \"trap\") (result i32) i32.const 1000000 i32.load)", TrapCode.MemoryOutOfBounds),
    ("(func (export \"trap\") (result i32) unreachable)", TrapCode.Unreachable),
    ("(func (export \"trap\") (result i32) i32.const 1 i32.const 0 i32.div_s)", TrapCode.IntegerDivisionByZero),
    ("(func $f (export \"trap\") (result i32) call $f)", TrapCode.StackOverflow)
})
    Case("catch-trap-" + expected, () =>
    {
        using var executor = new WasmExecutor([new("trap", Code("(module " + body + ")"))], new(Fuel: 10_000_000));
        try { executor.Invoke("trap", c => c.ReadInt("trap")); }
        catch (TrapException error) when (error.Type == expected) { return; }
        throw new InvalidOperationException("Expected trap: " + expected);
    });
Case("repeat-create-release", () =>
{
    for (var i = 0; i < 64; i++)
    {
        using var executor = new WasmExecutor([new("sample", sample)]);
        Check(executor.Invoke("sample", c => c.ReadInt("bgb_api")) == 1, "Repeat failed");
    }
});

#if UNTYPED_CANDIDATE
Case("reject-missing-export", () =>
{
    using var executor = new WasmExecutor([new("sample", sample)]);
    Reject(() => executor.Invoke("sample", c => c.ReadInt("missing")), "Missing WASM export");
});
Case("reject-signature-before-execution", () =>
{
    foreach (var signature in new[] { "(result i64)", "(result i32 i32)", "(param i32) (result i32)", "" })
    {
        using var executor = new WasmExecutor([new("wrong", Code("(module (func (export \"run\") " + signature + " unreachable))"))]);
        Reject(() => executor.Invoke("wrong", c => c.ReadInt("run")), "Missing WASM export");
    }
    using var wrongArguments = new WasmExecutor([new("wrong", Code("(module (func (export \"run\") (param i64 i32) (result i32) unreachable))"))]);
    Reject(() => wrongArguments.Invoke("wrong", c => c.ApplyInt("run", 1, 2)), "Missing WASM export");
});
Case("signed-int32-boundaries", () =>
{
    using var executor = new WasmExecutor([new("sample", sample)]);
    Check(executor.Invoke("sample", c => c.ApplyInt("bgb_command", int.MaxValue, 1)) == int.MinValue, "Int32 wrapping changed");
    Check(executor.Invoke("sample", c => c.ApplyInt("bgb_command", -2, 1)) == -1, "Signed integers changed");
});
#endif

using var output = new MemoryStream();
using (var writer = new Utf8JsonWriter(output, new() { Indented = true }))
{
    writer.WriteStartObject();
    writer.WriteBoolean("success", results.All(r => r.Passed));
    writer.WriteBoolean("dynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
    writer.WriteString("framework", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    writer.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    writer.WriteString("architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
    writer.WriteStartArray("checks");
    foreach (var result in results)
    {
        writer.WriteStartObject(); writer.WriteString("name", result.Name);
        writer.WriteBoolean("passed", result.Passed); writer.WriteString("detail", result.Detail); writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteStartArray("nativeModules");
    using var process = Process.GetCurrentProcess();
    foreach (ProcessModule module in process.Modules) writer.WriteStringValue(module.FileName);
    writer.WriteEndArray(); writer.WriteEndObject();
}
var json = System.Text.Encoding.UTF8.GetString(output.ToArray());
if (args.Length == 1) File.WriteAllText(args[0], json);
Console.WriteLine(json);
return results.All(r => r.Passed) ? 0 : 1;
