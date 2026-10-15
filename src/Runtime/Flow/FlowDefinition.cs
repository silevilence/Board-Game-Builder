using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

// Author data only. The editor and CLI share this contract with the runtime.
public sealed record FlowDefinition(int Format, List<FlowNode> Nodes, List<FlowEdge> Edges);
public sealed record FlowScript(string Path, string Entry = "execute");
public sealed record FlowSeat(string? Fixed = null, bool Current = false, FlowScript? Script = null);
public sealed record FlowValue(JsonElement? Literal = null, string? Path = null);
public sealed record FlowCondition(string Op, FlowValue? Left = null, FlowValue? Right = null,
    List<FlowCondition>? Items = null, FlowScript? Script = null);
public sealed record FlowAction(string Id, string Label, List<Command> Commands, bool Complete = true, string Result = "done");
public sealed record FlowNode
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public List<string> Results { get; init; } = ["done"];
    public List<Command> Commands { get; init; } = [];
    public FlowScript? Script { get; init; }
    public SortedDictionary<string, JsonElement> Parameters { get; init; } = new(StringComparer.Ordinal);
    public FlowSeat? Seat { get; init; }
    public List<FlowAction> Actions { get; init; } = [];
    public FlowScript? ActionsScript { get; init; }
    public FlowScript? DecideScript { get; init; }
    public List<string>? Winners { get; init; }
}
public sealed record FlowEdge(string Id, string From, string To, string Result = "done",
    FlowCondition? Condition = null, int Priority = 0, bool Default = false);
public sealed record FlowOutcome
{
    public List<Command> Commands { get; init; } = [];
    public string Result { get; init; } = "done";
    public bool Complete { get; init; } = true;
    public SortedDictionary<string, JsonElement>? GameContext { get; init; }
    public SortedDictionary<string, JsonElement>? NodeContext { get; init; }
    public List<string>? Winners { get; init; }
}
public sealed record FlowDiagnostic(string Code, string Message, string? Node = null, string? Edge = null, string Severity = "error");

public static class FlowFile
{
    public const int Version = 1;
    public const int MaxNodes = 1024;
    public const int MaxEdges = 8192;
    public static FlowDefinition Parse(string json)
    {
        try
        {
            if (json.Length > 1024 * 1024) throw new InvalidDataException("flow: file size limit exceeded");
            var value = Wire.Read<FlowDefinition>(json);
            if (value.Format != Version) throw new InvalidDataException("flow.format: unsupported version " + value.Format);
            if (value.Nodes == null || value.Edges == null) throw new InvalidDataException("flow: nodes and edges are required");
            if (value.Nodes.Count > MaxNodes || value.Edges.Count > MaxEdges) throw new InvalidDataException("flow: graph size limit exceeded");
            if (value.Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Kind)) ||
                value.Edges.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.To)))
                throw new InvalidDataException("flow: node id/kind and edge id/from/to are required");
            return value;
        }
        catch (JsonException error) { throw new InvalidDataException("flow: " + error.Message, error); }
    }
    public static string Write(FlowDefinition definition) => Wire.Json(definition);
    public static IEnumerable<FlowScript> Scripts(FlowDefinition definition)
    {
        foreach (var node in definition.Nodes)
            foreach (var script in new[] { node.Script, node.Seat?.Script, node.ActionsScript, node.DecideScript })
                if (script != null) yield return script;
        foreach (var edge in definition.Edges)
            foreach (var script in ConditionScripts(edge.Condition)) yield return script;
    }
    private static IEnumerable<FlowScript> ConditionScripts(FlowCondition? condition)
    {
        if (condition?.Script != null) yield return condition.Script;
        foreach (var child in condition?.Items ?? [])
            foreach (var script in ConditionScripts(child)) yield return script;
    }
}
