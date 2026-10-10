using BoardGame.Authoring;

namespace BoardGame.SampleAuthor;

public sealed class SamplePackage : IAuthorPluginPackage
{
    public IReadOnlyList<AuthorPlugin> Plugins
    {
        get
        {
            var info = new AuthorOperationInfo("sample.count", "sample.author", "Count files in the current project view", new(),
                new() { ["type"] = "object", ["properties"] = new System.Text.Json.Nodes.JsonObject { ["count"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "integer" } } }, [], []);
            return [new("sample.author", "1", 1, [], ["author.operations"], scope =>
                scope.Register(new(info, call => Task.FromResult<object>(new { call.View.Source, count = call.View.Paths.Count }))),
                "Example author operation without CLI changes") { Operations = [info] }];
        }
    }
}
