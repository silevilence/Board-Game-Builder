using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoardGame.Authoring;

public sealed record AuthorParameter(string Type, bool Required, string Description);
public sealed record AuthorOperationInfo(string Id, string Plugin, string Description,
    Dictionary<string, AuthorParameter> Parameters, JsonObject ResultSchema, string[] Conditions, string[] Effects)
{
    public bool ReadsProject { get; init; } = true;
}
public sealed record AuthorOperation(AuthorOperationInfo Info, Func<AuthorCall, Task<object>> Invoke);
public sealed record AuthorCall(ProjectView? Project, IProjectEditMode Mode, GameAuthorFormats Formats,
    JsonElement Arguments, string RequestId, CancellationToken Cancellation)
{
    public ProjectView View => Project ?? throw new InvalidOperationException("This operation did not request a project view");
    public string String(string key) => Arguments.GetProperty(key).GetString()!;
    public string? OptionalString(string key) => Arguments.TryGetProperty(key, out var value) ? value.GetString() : null;
    public int Integer(string key, int fallback) => Arguments.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
}
public sealed class AuthorOperationException(string code, string message, string? path = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Path { get; } = path;
}

public sealed class AuthorOperations
{
    private readonly Dictionary<string, AuthorOperation> entries = new(StringComparer.Ordinal);
    public IDisposable Register(AuthorOperation operation)
    {
        if (!entries.TryAdd(operation.Info.Id, operation)) throw new InvalidDataException("operation.duplicate: " + operation.Info.Id);
        return new AuthorCleanup(() => entries.Remove(operation.Info.Id));
    }
    public async Task<object> Invoke(string id, AuthorCall call)
    {
        if (!entries.TryGetValue(id, out var operation)) throw new AuthorOperationException("operation.unavailable", "Enable the operation's plugin in the selected profile: " + id);
        Validate(operation.Info, call.Arguments);
        call.Cancellation.ThrowIfCancellationRequested();
        return await operation.Invoke(call);
    }
    public static void Validate(AuthorOperationInfo info, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object) throw new AuthorOperationException("arguments.invalid", "Expected an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in args.EnumerateObject())
        {
            if (!names.Add(value.Name) || !info.Parameters.TryGetValue(value.Name, out var parameter)) throw new AuthorOperationException("arguments.invalid", "Unknown or duplicate parameter: " + value.Name);
            var valid = parameter.Type switch
            {
                "string" => value.Value.ValueKind == JsonValueKind.String,
                "integer" => value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out _),
                "boolean" => value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "object" => value.Value.ValueKind == JsonValueKind.Object,
                "array" => value.Value.ValueKind == JsonValueKind.Array,
                _ => false
            };
            if (!valid) throw new AuthorOperationException("arguments.invalid", value.Name + ": expected " + parameter.Type);
        }
        foreach (var parameter in info.Parameters.Where(p => p.Value.Required))
            if (!names.Contains(parameter.Key)) throw new AuthorOperationException("arguments.missing", parameter.Key);
    }
}
public sealed class AuthorCleanup(Action action) : IDisposable
{
    private Action? cleanup = action;
    public void Dispose() => Interlocked.Exchange(ref cleanup, null)?.Invoke();
}

public static class FileOperations
{
    public static AuthorOperationInfo WriteInfo => new("files.write", "author.files", "Apply UTF-8 file changes; null deletes a file. Prefer direct file editing when suitable.",
        new() { ["files"] = new("object", true, "Relative path to UTF-8 text or null map") },
        new() { ["type"] = "object", ["description"] = "EditReceipt: requestId, state, saved, source, resultVersion, diagnostics, appliedPaths" }, [], ["project"]);
    public static AuthorOperationInfo ResultInfo => new("request.result", "author.files", "Inspect a retained write receipt without repeating the write",
        new() { ["request"] = new("string", true, "Original request ID") }, WriteInfo.ResultSchema.DeepClone().AsObject(), [], []) { ReadsProject = false };
    public static AuthorOperationInfo RecoverInfo => ResultInfo with { Id = "request.recover", Description = "Explicitly resume a partial write if every baseline still matches; preserve external changes", Effects = ["recovery"] };
    public static AuthorOperationInfo SaveInfo => new("workspace.save", "author.files", "Explicitly save the connected workspace to disk", new(), WriteInfo.ResultSchema.DeepClone().AsObject(), ["GUI edit mode"], ["save"]);
    public static AuthorOperationInfo ListInfo => new("files.list", "author.files", "Enumerate the selected project view",
        new() { ["prefix"] = new("string", false, "Relative path prefix") },
        JsonNode.Parse("""{"type":"object","required":["source","paths"]}""")!.AsObject(), [], []);
    public static AuthorOperationInfo ReadInfo => new("files.read", "author.files", "Read UTF-8 text from the selected project view",
        new() { ["path"] = new("string", true, "Relative project file") },
        JsonNode.Parse("""{"type":"object","required":["source","text"]}""")!.AsObject(), ["File exists"], []);
    public static AuthorPlugin Plugin => new("author.files", "1", 1, [], ["author.operations"], scope =>
    {
        scope.Register(new(ListInfo, call => Task.FromResult<object>(new { call.View.Source, paths = call.View.Enumerate(call.OptionalString("prefix") ?? "") })));
        scope.Register(new(ReadInfo, call => Task.FromResult<object>(new { call.View.Source, text = call.View.Text(call.String("path")) })));
        scope.Register(new(WriteInfo, async call =>
        {
            var files = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in call.Arguments.GetProperty("files").EnumerateObject())
            {
                if (file.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new AuthorOperationException("arguments.invalid", "Expected text or null", file.Name);
                if (!files.TryAdd(file.Name, file.Value.ValueKind == JsonValueKind.Null ? null : System.Text.Encoding.UTF8.GetBytes(file.Value.GetString()!))) throw new AuthorOperationException("arguments.invalid", "Duplicate path", file.Name);
            }
            return await call.Mode.Apply(call.View.Change(call.RequestId, files), call.Cancellation);
        }));
        scope.Register(new(ResultInfo, async call => await call.Mode.Result(call.String("request"), call.Cancellation)));
        scope.Register(new(RecoverInfo, async call => call.Mode is IRecoverableEditMode requests
            ? await requests.Recover(call.String("request"), call.Cancellation)
            : throw new AuthorOperationException("recovery.unavailable", "The selected mode does not expose recovery")));
        scope.Register(new(SaveInfo, async call => call.Mode is GuiEditMode gui ? await gui.Save(call.RequestId, call.Cancellation)
            : throw new AuthorOperationException("mode.unsupported", "Explicit workspace saving requires GUI mode")));
    }, "Generic project file access") { Operations = [ListInfo, ReadInfo, WriteInfo, ResultInfo, RecoverInfo, SaveInfo] };
}
