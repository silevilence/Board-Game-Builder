using System.Text.Json;

namespace BoardGame.Kernel;

// Authority-only continuation data. Never include contexts or trace in a seat projection.
public sealed record FlowState
{
    public required string DefinitionHash { get; init; }
    public required string Node { get; init; }
    public string Status { get; init; } = "ready";
    public string? Seat { get; init; }
    public long Revision { get; init; }
    public string? Result { get; init; }
    public string? Edge { get; init; }
    public SortedDictionary<string,JsonElement> GameContext { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,JsonElement> NodeContext { get; init; } = new(StringComparer.Ordinal);
    public List<FlowTrace> Trace { get; init; } = [];
    public void Validate(GameState state)
    {
        if(string.IsNullOrWhiteSpace(DefinitionHash) || string.IsNullOrWhiteSpace(Node) || Revision<0 ||
            Status is not ("ready" or "waiting" or "ended") || (Status=="waiting")!=(Seat!=null) ||
            Seat!=null && !state.Seats.Any(s=>s.Id==Seat) || (Status=="ended")!=state.Ended ||
            GameContext==null || NodeContext==null || Trace==null || Trace.Count>64 ||
            System.Text.Encoding.UTF8.GetByteCount(Wire.Json(new {GameContext,NodeContext}))>16384)
            throw new InvalidDataException("flow-state: invalid continuation or context budget exceeded");
    }
}
public sealed record FlowTrace(long Revision,string Node,string? Result,string? Edge);
// Engine/plugin-owned diagnostic code; never populate from script error text or returned data.
public sealed class FlowOperationException(string code) : Exception(code);
