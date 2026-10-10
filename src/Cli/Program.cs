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
        Write(new { commands = new[] { "skill install|repair|status|uninstall --target <agent-skills-directory> [--method copy|junction]", "docs [topic]", "workspaces", "profile", "plugins", "operations", "describe <operation>", "call <operation> --args <JSON>" },
            options = new[] { "--project <directory>", "--profile <name>", "--request <stable-id>", "--args-file <JSON-file>" },
            exits = new { success = 0, failure = 1, arguments = 2, conflict = 3, unknownOrPartial = 4, cancelled = 130 } });
        return 0;
    }
    if(args[0]=="skill")
    {
        if(args.Length<4 || args[2]!="--target")throw new ArgumentException("skill <install|repair|status|uninstall> --target <directory> [--method copy|junction]");
        var method=args.Length==6 && args[4]=="--method"?args[5]:args.Length==4?null:throw new ArgumentException("Invalid skill options");
        var cli=Path.Combine(AppContext.BaseDirectory,"bgb.exe");var docs=Path.Combine(AppContext.BaseDirectory,"docs","author");
        var skillResult=args[1] switch
        {
            "install" or "repair"=>SkillInstaller.Install(args[3],cli,docs,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"templates","bgb-author","SKILL.md.template")),method ?? SkillInstaller.Status(args[3]).Installation?.Method ?? "copy",args[1]=="repair"),
            "status"=>SkillInstaller.Status(args[3]),
            "uninstall"=>SkillInstaller.Uninstall(args[3]),
            _=>throw new ArgumentException("Unknown skill command")
        };
        Write(skillResult);return args[1]=="uninstall"?!skillResult.Installed?0:1:skillResult.Healthy?0:1;
    }
    if(args[0]=="docs")
    {
        var documents=AuthorDocuments.Installed(AuthorPluginPackages.Discover(Path.Combine(AppContext.BaseDirectory,"plugins","author")));
        object value=args.Length==1?documents.Select(d=>new {d.Topic,d.Plugin,d.Title}):documents.SingleOrDefault(d=>d.Topic==args[1]) ?? throw new ArgumentException("Unknown documentation topic");
        Write(new {version=AuthorDocuments.Version,result=value});return 0;
    }
    if(args[0]=="workspaces") { Write(WorkspaceConnections.Discover());return 0; }
    string? project = null, profile = null, operation = null, instance = null;
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
            case "--instance": instance = args[++i]; break;
            case "--args": json = args[++i]; break;
            case "--args-file": json = File.ReadAllText(args[++i]); break;
            case "--request": request = args[++i]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    var effective = AuthorProfiles.Resolve(AuthorProfiles.Locate(project, Environment.CurrentDirectory), profile);
    if(instance!=null) effective=effective with {Profile=effective.Profile with {Workspace=instance}};
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
