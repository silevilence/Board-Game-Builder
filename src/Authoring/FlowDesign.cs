using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record FlowPoint(double X, double Y);
public sealed record FlowDesignView
{
    public int Format { get; init; } = 1;
    public Dictionary<string, FlowPoint> Positions { get; init; } = [];
    public Dictionary<string, string> Names { get; init; } = [];
    public double Zoom { get; init; } = 1;
    public double PanX { get; init; }
    public double PanY { get; init; }
}

// Design metadata is never referenced by the runtime startup or flow definition.
public static class FlowDesign
{
    public static string ViewPath(string flowPath) => flowPath + ".design.json";
    public static FlowDesignView ReadView(ProjectView view, string path)
    {
        var result = view.Exists(ViewPath(path)) ? Wire.Read<FlowDesignView>(view.Text(ViewPath(path))) : new();
        if (result.Format != 1 || result.Positions == null || result.Names == null ||
            !double.IsFinite(result.Zoom) || result.Zoom is < .2 or > 3 ||
            !double.IsFinite(result.PanX) || !double.IsFinite(result.PanY) ||
            result.Positions.Values.Any(p => p == null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidDataException("Invalid flow design view");
        return result;
    }
    public static ProjectChanges Create(ProjectView view, string path)
    {
        ProjectPaths.Validate(path);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || view.Exists(path) || view.Exists(ViewPath(path)))
            throw new InvalidDataException("Choose a new JSON flow path");
        var startup = JsonNode.Parse(view.Text(GameRuntime.Entry))!.AsObject();
        var plugins = startup["plugins"]!.AsArray();
        if (plugins.Any(p => p!["id"]!.GetValue<string>() == "flow")) throw new InvalidDataException("Flow already enabled; open the existing graph");
        // This operation is explicitly labelled as switching the flow controller in the UI.
        foreach (var p in plugins.Where(p => p!["id"]!.GetValue<string>() is "lua.rules" or "rules.commands").ToArray()) plugins.Remove(p);
        plugins.Add(new JsonObject { ["id"] = "flow", ["version"] = "1", ["api"] = 1,
            ["configuration"] = new JsonObject { ["path"] = path } });
        var graph = new FlowDefinition(1, [new() { Id = "start", Kind = "start" }, new() { Id = "end", Kind = "end", Winners = [] }], [new("finish", "start", "end")]);
        return view.Change(Guid.NewGuid().ToString("N"), new Dictionary<string, byte[]?> {
            [GameRuntime.Entry] = Encoding.UTF8.GetBytes(startup.ToJsonString()),
            [path] = Encoding.UTF8.GetBytes(FlowFile.Write(graph)),
            [ViewPath(path)] = Encoding.UTF8.GetBytes(Wire.Json(new FlowDesignView { Positions = new() { ["start"] = new(60, 100), ["end"] = new(380, 100) } })) });
    }
    public static ProjectChanges Change(ProjectView view, FlowDefinition graph, FlowDesignView? design = null,
        IReadOnlyDictionary<string, string>? scripts = null)
    {
        var format = FlowOperations.Format(view);
        var candidate = JsonNode.Parse(FlowFile.Write(graph))!.AsObject();
        var serialized = format.Serialize(view, candidate);
        var files = new Dictionary<string, byte[]?>();
        if (!JsonNode.DeepEquals(format.Read(view), candidate)) files[format.DefinitionPath] = Encoding.UTF8.GetBytes(serialized);
        if (design != null) files.Add(ViewPath(format.DefinitionPath), Encoding.UTF8.GetBytes(Wire.Json(design)));
        foreach (var file in scripts ?? new Dictionary<string, string>())
        {
            ProjectPaths.Validate(file.Key);
            if (!file.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Node scripts must use a .lua path");
            files.Add(file.Key, Encoding.UTF8.GetBytes(file.Value));
        }
        return view.Change(Guid.NewGuid().ToString("N"), files);
    }
}
