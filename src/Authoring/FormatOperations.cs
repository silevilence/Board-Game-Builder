using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public static class FormatOperations
{
    private static AuthorOperationInfo CreateInfo(string format) => new("project.create." + format, "author." + format,
        "Create a minimal " + format + " project without overwriting existing template files",
        new() { ["id"] = new("string", false, "Game identity, default untitled"), ["definition"] = new("string", false, "Relative definition path") },
        FileOperations.WriteInfo.ResultSchema.DeepClone().AsObject(), ["Template paths do not exist"], ["project"]);
    public static AuthorPlugin Module => FormatPlugin("module", GameAuthorFormats.ModuleProvider, GameAuthorFormats.Template);
    public static AuthorPlugin State => FormatPlugin("state", GameAuthorFormats.StateProvider, StateTemplate);
    private static AuthorPlugin FormatPlugin(string name, GameAuthorFormatProvider provider, Func<Dictionary<string, byte[]>> template)
    {
        var info = CreateInfo(name);
        return new("author." + name, "1", 1, [], ["author.formats", "author.operations"], scope =>
        {
            scope.Own(scope.Service<GameAuthorFormats>("author.formats").Register(provider));
            scope.Register(new(info, async call =>
            {
                var files = template();
                var startup = JsonNode.Parse(files[GameRuntime.Entry])!.AsObject();
                if (call.OptionalString("id") is { } id)
                {
                    if (string.IsNullOrWhiteSpace(id)) throw new AuthorOperationException("arguments.invalid", "Game identity must not be empty");
                    startup["id"] = id;
                }
                if (call.OptionalString("definition") is { } definition)
                {
                    ProjectPaths.Validate(definition);
                    var loader = startup["plugins"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == provider.LoaderId)!;
                    var previous = loader["configuration"]!["path"]!.GetValue<string>();
                    var bytes = files[previous]; files.Remove(previous);
                    if (!files.TryAdd(definition, bytes)) throw new AuthorOperationException("template.path", "Definition collides with another template file", definition);
                    loader["configuration"]!["path"] = definition;
                }
                files[GameRuntime.Entry] = Encoding.UTF8.GetBytes(startup.ToJsonString(new() { WriteIndented = true }));
                foreach (var path in files.Keys)
                    if (call.View.Exists(path)) throw new AuthorOperationException("template.conflict", "Template file already exists", path);
                return await call.Mode.Apply(call.View.Change(call.RequestId, files.ToDictionary(p => p.Key, p => (byte[]?)p.Value)), call.Cancellation);
            }));
        }, "Author support and template for definition." + name) { Operations = [info] };
    }
    private static Dictionary<string, byte[]> StateTemplate()
    {
        var files = GameAuthorFormats.Template();
        files.Remove("module.json");
        files["position.json"] = Encoding.UTF8.GetBytes("""
            {"format":1,"state":{"seats":[{"id":"p1","team":"one","controller":"human"}],"phases":["play"],"phase":"play","entities":{}},"resources":{}}
            """);
        var startup = JsonNode.Parse(files[GameRuntime.Entry])!;
        var loader = startup["plugins"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "definition.module")!;
        loader["id"] = "definition.state"; loader["configuration"]!["path"] = "position.json";
        files[GameRuntime.Entry] = Encoding.UTF8.GetBytes(startup.ToJsonString());
        return files;
    }
    private static AuthorOperationInfo ReadInfo => new("configuration.read", "author.configuration", "Read the selected format's author model", new(),
        new() { ["type"] = "object", ["description"] = "source, definition path and format-specific configuration object" }, ["Matching author format plugin"], []);
    private static AuthorOperationInfo ReplaceInfo => ReadInfo with
    {
        Id = "configuration.replace", Description = "Serialize a complete author model through the selected format provider",
        Parameters = new() { ["configuration"] = new("object", true, "Complete format-specific model from configuration.read"), ["version"] = new("string", true, "Project version returned by configuration.read") },
        ResultSchema = FileOperations.WriteInfo.ResultSchema.DeepClone().AsObject(), Effects = ["project"]
    };
    public static AuthorPlugin Configuration => new("author.configuration", "1", 1, [], ["author.operations"], scope =>
    {
        FlowOperations.Register(scope);
        scope.Register(new(ReadInfo, call =>
        {
            var format = Resolve(call);
            return Task.FromResult<object>(new { call.View.Source, definition = format.DefinitionPath, configuration = format.Read(call.View) });
        }));
        scope.Register(new(ReplaceInfo, async call =>
        {
            if (call.String("version") != call.View.Source.Version) throw new AuthorOperationException("project.conflict", "Configuration was read from a different project version");
            var format = Resolve(call);
            var configuration = JsonNode.Parse(call.Arguments.GetProperty("configuration").GetRawText())!.AsObject();
            var data = format.Serialize(call.View, configuration);
            return await call.Mode.Apply(call.View.Change(call.RequestId, new Dictionary<string, byte[]?> { [format.DefinitionPath] = Encoding.UTF8.GetBytes(data) }), call.Cancellation);
        }));
    }, "Format-independent structured author operations") { Operations = [ReadInfo, ReplaceInfo,..FlowOperations.Operations] };
    private static IGameAuthorFormat Resolve(AuthorCall call) => call.Formats.Resolve(call.View)
        ?? throw new AuthorOperationException("format.unavailable", "Enable matching author.module or author.state support, or use files.read/files.write and direct editing", GameRuntime.Entry);
}
