using System.Text.Json;
using BoardGame.Authoring;

var output = Console.Out;
Console.SetOut(Console.Error);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
void Write(object value) => output.WriteLine(JsonSerializer.Serialize(value, AuthorProfiles.Json));
try
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Write(new { commands = new[] { "profile", "plugins", "operations", "describe <operation>", "call <operation> --args <JSON>" },
            options = new[] { "--project <directory>", "--profile <name>", "--request <stable-id>", "--args-file <JSON-file>" },
            exits = new { success = 0, failure = 1, arguments = 2, conflict = 3, unknownOrPartial = 4, cancelled = 130 } });
        return 0;
    }
    string? project = null, profile = null, operation = null;
    var json = "{}"; var request = Guid.NewGuid().ToString("N");
    var offset = 1;
    if (args[0] is "call" or "describe")
    {
        if (args.Length < 2) throw new ArgumentException("Operation ID required");
        operation = args[1]; offset = 2;
    }
    for (var i = offset; i < args.Length; i++)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Missing option value: " + args[i]);
        switch (args[i])
        {
            case "--project": project = args[++i]; break;
            case "--profile": profile = args[++i]; break;
            case "--args": json = args[++i]; break;
            case "--args-file": json = File.ReadAllText(args[++i]); break;
            case "--request": request = args[++i]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    var effective = AuthorProfiles.Resolve(AuthorProfiles.Locate(project, Environment.CurrentDirectory), profile);
    await using var host = await AuthorHost.Start(effective, AuthorPluginPackages.Discover(Path.Combine(AppContext.BaseDirectory, "plugins", "author")));
    object result = args[0] switch
    {
        "profile" => new { effective, mode = host.Enabled.Single(p => p.Provides.Contains("author.mode")).Id, plugins = host.Enabled.Select(p => p.Id) },
        "plugins" => host.Installed.Select(p => new { p.Id, p.Version, p.Api, p.Description, enabled = host.Enabled.Any(e => e.Id == p.Id), p.Requires, p.Provides }),
        "operations" => host.Installed.SelectMany(p => p.Operations.Select(o => new { operation = o, enabled = host.Enabled.Any(e => e.Id == p.Id), reason = host.Enabled.Any(e => e.Id == p.Id) ? null : "Plugin not selected by profile" })),
        "describe" => host.Installed.SelectMany(p => p.Operations).SingleOrDefault(o => o.Id == operation) ?? throw new AuthorOperationException("operation.unknown", operation!),
        "call" => await host.Invoke(operation!, JsonSerializer.Deserialize<JsonElement>(json), request, cancellation.Token),
        _ => throw new ArgumentException("Unknown command: " + args[0])
    };
    var code = result is IAuthorOutcome outcome ? outcome.ExitCode : result is EditReceipt receipt ? receipt.State switch { EditState.Applied => 0, EditState.Conflict => 3, EditState.Unknown or EditState.PartialFailure => 4, _ => 1 } : 0;
    Write(new { ok = code == 0, requestId = request, result });
    return code;
}
catch (OperationCanceledException) { Write(new { ok = false, code = "operation.cancelled", message = "Cancelled" }); return 130; }
catch (Exception error)
{
    Write(new { ok = false, code = error is AuthorOperationException operation ? operation.Code : error is ArgumentException or JsonException ? "arguments.invalid" : "author.failure",
        message = error.Message, path = (error as AuthorOperationException)?.Path });
    return error is ArgumentException or JsonException || error is AuthorOperationException e && e.Code.StartsWith("arguments.") ? 2 : 1;
}
