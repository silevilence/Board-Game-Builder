using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoardGame.Authoring;

public sealed record AuthorParameter(string Type, bool Required, string Description);
public sealed record AuthorOperationInfo(string Id, string Plugin, string Description,
    Dictionary<string, AuthorParameter> Parameters, JsonObject ResultSchema, string[] Conditions, string[] Effects);
public sealed record AuthorOperation(AuthorOperationInfo Info, Func<AuthorCall, Task<object>> Invoke);
public sealed record AuthorCall(ProjectView View, IProjectEditMode Mode, GameAuthorFormats Formats,
    JsonElement Arguments, string RequestId, CancellationToken Cancellation)
{
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
    }, "Generic project file access") { Operations = [ListInfo, ReadInfo] };
}
