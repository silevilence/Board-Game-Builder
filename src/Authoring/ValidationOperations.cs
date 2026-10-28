using System.Text.Json;
using BoardGame.AppRuntime;

namespace BoardGame.Authoring;

public interface IAuthorOutcome { bool Success { get; } int ExitCode { get; } }
public sealed record ValidationResult(ProjectSource Source, string[] Checks, AuthorDiagnostic[] Diagnostics, bool CurrentAtCompletion) : IAuthorOutcome
{
    public bool Success => Diagnostics.Length == 0;
    public int ExitCode => Success ? 0 : 1;
}
public static class ValidationOperations
{
    private static readonly string[] Layers = ["files", "configuration", "dependencies", "runtime"];
    public static AuthorOperationInfo Info => new("project.validate", "author.validation", "Validate an isolated version of the selected project view without saving it",
        new() { ["layer"] = new("string", false, "Maximum layer: files, configuration, dependencies or runtime (default); preceding checks included") },
        new() { ["type"] = "object", ["description"] = "source, checks actually executed, diagnostics with file/line when known, success, currentAtCompletion" }, [], []);
    public static AuthorPlugin Plugin => new("author.validation", "1", 1, [], ["author.operations"], s => s.Register(new(Info, Validate)),
        "Layered project and runtime validation") { Operations = [Info] };
    private static async Task<object> Validate(AuthorCall call)
    {
        var layer = call.OptionalString("layer") ?? "runtime";
        var last = Array.IndexOf(Layers, layer);
        if (last < 0) throw new AuthorOperationException("arguments.invalid", "Unknown validation layer: " + layer);
        var checks = new List<string>(); var diagnostics = new List<AuthorDiagnostic>();

        for (var index = 0; index <= last; index++)
        {
            call.Cancellation.ThrowIfCancellationRequested();
            var check = Layers[index]; checks.Add(check);
            try
            {
                switch (check)
                {
                    case "files":
                        if (!call.View.Exists(GameRuntime.Entry)) throw new AuthorOperationException("entry.missing", "Missing game.start.json; create or repair the startup entry", GameRuntime.Entry);
                        foreach (var path in call.View.Paths.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                        {
                            try { using var json = JsonDocument.Parse(call.View.Bytes(path)); }
                            catch (JsonException error) { diagnostics.Add(new("json.invalid", error.Message, path, error.LineNumber + 1, error.BytePositionInLine + 1)); }
                        }
                        break;
                    case "configuration":
                        checks.AddRange(await call.Runtimes.Resolve(call.View).Validate(call.View,check,call.Cancellation));
                        var format = call.Formats.Resolve(call.View);
                        if (format != null) _ = format.Read(call.View);
                        break;
                    case "dependencies":
                    case "runtime":
                        checks.AddRange(await call.Runtimes.Resolve(call.View).Validate(call.View,check,call.Cancellation));
                        break;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            {
                var path = (error as AuthorOperationException)?.Path ?? call.View.Paths.FirstOrDefault(p => error.Message.Contains(p, StringComparison.Ordinal));
                diagnostics.Add(new((error as AuthorOperationException)?.Code ?? "validation." + check, error.Message, path));
            }
            if (diagnostics.Count > 0) break;
        }
        var latest = await call.Mode.Read(call.Cancellation);
        return new ValidationResult(call.View.Source, checks.ToArray(), diagnostics.ToArray(), latest.Source == call.View.Source);
    }
}
