namespace BoardGame.Kernel;
public sealed record Command
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? FlowRevision {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SecretDeclaration? Collection {get;init;}
    public string Kind { get; init; } = "";
    public string? Entity { get; init; }
    public string? Target { get; init; }
    public string? Property { get; init; }
    public int Value { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public string? Seat { get; init; }
    public string? Phase { get; init; }
    public string? Visibility { get; init; }
    public List<string>? Winners { get; init; }
}
public sealed record Submission(long Sequence, string Actor, Command Command, bool Accepted, string Reason,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Execution=null);
public sealed record CommandResult(long Sequence, bool Accepted, string Reason);
