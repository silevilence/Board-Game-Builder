namespace BoardGame.Kernel;
// This object belongs to the authority host. Consumers receive detached data only.
public sealed partial class GameEngine
{
    private GameState state;
    private readonly List<Submission> submissions = [];
    public GameEngine(GameState initial) => state = Wire.Copy(initial);
    public GameState ExportAuthorityState() => Wire.Copy(state);
    public IReadOnlyList<Submission> ExportAuthoritySubmissions() => Wire.Copy(submissions);
    public CommandResult Submit(string actor, Command command)
    {
        var sequence = submissions.Count + 1L;
        try
        {
            if (!state.Seats.Any(s => s.Id == actor) || state.Ended) throw new CommandRejected("operation-not-allowed");
            var candidate = Wire.Copy(state);
            Apply(candidate, actor, command);
            state = candidate;
            submissions.Add(new(sequence, actor, Wire.Copy(command), true, "accepted"));
            return new(sequence, true, "accepted");
        }
        catch (CommandRejected error)
        {
            submissions.Add(new(sequence, actor, Wire.Copy(command), false, error.Message));
            return new(sequence, false, error.Message);
        }
    }
    private static Entity Access(GameState state, string actor, string? id)
    {
        if (id == null || !state.Entities.TryGetValue(id, out var entity) || (entity.Owner != null && entity.Owner != actor) || entity.Visibility == "hidden")
            throw new CommandRejected("operation-not-allowed");
        return entity;
    }
    private static void Apply(GameState state, string actor, Command command)
    {
        var entity = Access(state, actor, command.Entity);
        switch (command.Kind)
        {
            case "move":
                if (!double.IsFinite(command.X) || !double.IsFinite(command.Y)) throw new CommandRejected("invalid-position");
                entity.X = command.X; entity.Y = command.Y; break;
            case "flip":
                if (entity.Faces.Count == 0 || command.Value < 0 || command.Value >= entity.Faces.Count) throw new CommandRejected("invalid-face");
                entity.Face = command.Value; entity.FaceUp = !entity.FaceUp; break;
            case "set":
                if (command.Property == null || !entity.Properties.ContainsKey(command.Property)) throw new CommandRejected("invalid-property");
                entity.Properties[command.Property] = command.Value; break;
            case "transfer":
                var target = Access(state, actor, command.Target);
                if (!target.IsContainer || target.Contents.Count >= target.Capacity || entity.Container == target.Id) throw new CommandRejected("invalid-container");
                for (Entity? parent = target; parent != null; parent = parent.Container == null ? null : state.Entities[parent.Container])
                    if (parent.Id == entity.Id) throw new CommandRejected("invalid-container");
                if (entity.Container != null) Access(state, actor, entity.Container).Contents.Remove(entity.Id);
                target.Contents.Add(entity.Id); entity.Container = target.Id; break;
            default: throw new CommandRejected("unknown-command");
        }
    }
    private sealed class CommandRejected(string message) : Exception(message);
}
