using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record AuthorArtifact(ProjectSource Source, string Target, string Kind, string Fingerprint);
public static class ExportOperations
{
    private static AuthorOperationInfo PackageInfo => new("project.pack", "author.export", "Package the selected project version using the official game package protocol",
        new() { ["target"] = new("string", true, "New .bgm output file outside the project") },
        new() { ["type"] = "object", ["description"] = "source, target, kind and runtime fingerprint; independent output, not a project save" }, ["Valid runtime project"], ["artifact"]);
    private static AuthorOperationInfo ExportInfo => PackageInfo with
    {
        Id = "player.export", Description = "Export an independent Windows Player directory using the precompiled formal Player; no SDK invocation",
        Parameters = new() { ["target"] = new("string", true, "New independent output directory"), ["player"] = new("string", false, "Formal Player distribution; default bundled player directory"),
            ["components"] = new("string",false,"Optional directory of verified release component ZIPs for offline export"),
            ["dependencyCache"] = new("string",false,"Optional same-volume export dependency cache"),
            ["offline"] = new("boolean",false,"Do not download missing export components") }
    };
    public static AuthorPlugin Plugin => new("author.export", "1", 1, [], ["author.operations"], scope =>
    {
        scope.Register(new(PackageInfo, call => Export(call, false)));
        scope.Register(new(ExportInfo, call => Export(call, true)));
    }, "Official game packaging and standalone Player export") { Operations = [PackageInfo, ExportInfo] };
    private static Task<object> Export(AuthorCall call, bool player)
    {
        call.Cancellation.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(call.String("target"));
        if (target.Equals(call.View.Source.Identity.Directory, StringComparison.OrdinalIgnoreCase) || target.StartsWith(
            Path.TrimEndingDirectorySeparator(call.View.Source.Identity.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new AuthorOperationException("output.project", "Choose an independent output outside the source project", target);
        using var snapshot = new ProjectSnapshot(call.View);
        var module = GameRuntime.Load(snapshot.Directory);
        if (player)
        {
            var distribution = Path.GetFullPath(call.OptionalString("player") ?? Path.Combine(AppContext.BaseDirectory, "player"));
            var offline=call.Arguments.TryGetProperty("offline",out var offlineValue) && offlineValue.GetBoolean();
            PlayerDistribution.Export(distribution, snapshot.Directory, target, _ => call.Cancellation.ThrowIfCancellationRequested(),
                dependencies:new(call.OptionalString("dependencyCache"),call.OptionalString("components"),!offline),cancellation:call.Cancellation);
        }
        else GamePackage.Pack(snapshot.Directory, target);
        return Task.FromResult<object>(new AuthorArtifact(call.View.Source, target, player ? "player-directory" : "game-package", module.Fingerprint));
    }
}
