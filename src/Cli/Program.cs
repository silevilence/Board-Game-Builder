using System.Text.Json;
using BoardGame.Authoring;

try
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { commands = new[] { "profile", "plugins" }, options = new[] { "--project <directory>", "--profile <name>" } }, AuthorProfiles.Json));
        return 0;
    }
    string? project = null, profile = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Missing option value: " + args[i]);
        switch (args[i])
        {
            case "--project": project = args[++i]; break;
            case "--profile": profile = args[++i]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    var effective = AuthorProfiles.Resolve(AuthorProfiles.Locate(project, Environment.CurrentDirectory), profile);
    await using var host = await AuthorHost.Start(effective);
    object result = args[0] switch
    {
        "profile" => new { effective, mode = host.Enabled.Single(p => p.Provides.Contains("author.mode")).Id, plugins = host.Enabled.Select(p => p.Id) },
        "plugins" => host.Installed.Select(p => new { p.Id, p.Version, p.Api, p.Description, enabled = host.Enabled.Any(e => e.Id == p.Id), p.Requires, p.Provides }),
        _ => throw new ArgumentException("Unknown command: " + args[0])
    };
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result }, AuthorProfiles.Json));
    return 0;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, code = error is ArgumentException ? "arguments.invalid" : "author.failure", message = error.Message }, AuthorProfiles.Json));
    return error is ArgumentException ? 2 : 1;
}
