namespace BoardGame.Kernel;
// This object belongs to the authority host. Consumers receive detached data only.
public sealed partial class GameEngine
{
    private GameState state;
    private readonly List<Submission> submissions = [];
    private readonly GameState initial;
    private readonly string fingerprint;
    private readonly uint seed;
    private readonly List<LogEntry> entries = [];
    private readonly IGameRules? rules;
    private readonly List<Diagnostic> diagnostics = [];
    public IReadOnlyList<Diagnostic> ExportDiagnostics() => Wire.Copy(diagnostics);
    public IReadOnlyList<ActionOffer> Actions(string actor)
    {
        var view = Project(actor);
        if (state.Ended || view.CurrentSeat != actor) return [];
        try { return Wire.Copy((rules?.Actions(view) ?? []).ToList()); }
        catch (Exception error) { diagnostics.Add(new(submissions.Count+1,actor,"actions",error.Message)); throw new InvalidDataException("Rule actions failed.",error); }
    }
    public GameEngine(GameState initial, uint seed = 1, string fingerprint = "development", IGameRules? rules = null)
    {
        state = Wire.Copy(initial); state.RandomState = seed == 0 ? 0x9E3779B9 : seed;
        this.rules = rules; this.initial = Wire.Copy(state); this.seed = seed; this.fingerprint = fingerprint;
    }
    public GameState ExportAuthorityState() => Wire.Copy(state);
    public IReadOnlyList<Submission> ExportAuthoritySubmissions() => Wire.Copy(submissions);
    public CommandResult Submit(string actor, Command command)
    {
        var sequence = submissions.Count + 1L;
        try
        {
            if (!state.Seats.Any(s => s.Id == actor) || state.Ended || state.Seats[state.CurrentSeat].Id != actor) throw new CommandRejected("operation-not-allowed");
            var candidate = Wire.Copy(state);
            List<AppliedCommand> effects = [];
            if (rules == null) effects.Add(Execute(candidate,actor,command));
            else
            {
                var decision = rules.Decide(Project(state,actor),Wire.Copy(command));
                if (!decision.Allowed) throw new CommandRejected("rule-rejected");
                if (decision.Commands.Count is < 1 or > 64) throw new CommandRejected("rule-command-limit");
                foreach (var effect in decision.Commands) effects.Add(Execute(candidate,actor,effect,true));
            }
            state = candidate;
            Record(new(sequence, actor, Wire.Copy(command), true, "accepted"), effects);
            return new(sequence, true, "accepted");
        }
        catch (CommandRejected error)
        {
            Record(new(sequence, actor, Wire.Copy(command), false, error.Message), []);
            return new(sequence, false, error.Message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            diagnostics.Add(new(sequence,actor,"decide",error.Message));
            Record(new(sequence,actor,Wire.Copy(command),false,"script-failure"),[]);
            return new(sequence,false,"script-failure");
        }
    }
    private static Entity Access(GameState state, string actor, string? id, bool ruleEffect = false)
    {
        if (id == null || !state.Entities.TryGetValue(id, out var entity) || (!ruleEffect && entity.Owner != null && entity.Owner != actor) || !IdentityVisible(state, entity, actor))
            throw new CommandRejected("operation-not-allowed");
        return entity;
    }
    private static AppliedCommand Execute(GameState state, string actor, Command command, bool ruleEffect = false)
    {
        Apply(state,actor,command,ruleEffect:ruleEffect);
        return new(Wire.Copy(command),ruleEffect,command.Kind == "shuffle" ? [..state.Entities[command.Entity!].Contents] : null,state.RandomState);
    }
    private static void Apply(GameState state, string actor, Command command, List<string>? recordedOrder = null, bool ruleEffect = false)
    {
        if (command.Kind is "next" or "phase" or "finish")
        {
            if (state.Ended) throw new CommandRejected("operation-not-allowed");
            if (command.Kind == "finish")
            {
                if (command.Winners == null || command.Winners.Distinct().Count() != command.Winners.Count || command.Winners.Any(w=>!state.Seats.Any(s=>s.Id==w))) throw new CommandRejected("invalid-winners");
                state.Ended=true; state.Winners=[..command.Winners]; return;
            }
            if (command.Phase != null && !state.Phases.Contains(command.Phase)) throw new CommandRejected("invalid-phase");
            if (command.Kind == "phase" && command.Phase == null) throw new CommandRejected("invalid-phase");
            if (command.Kind == "next") { state.CurrentSeat=(state.CurrentSeat+1)%state.Seats.Count; state.Turn++; }
            if (command.Phase != null) state.Phase=command.Phase;
            return;
        }
        var entity = Access(state, actor, command.Entity,ruleEffect);
        switch (command.Kind)
        {
            case "shuffle":
                if (!entity.IsContainer || !entity.Ordered) throw new CommandRejected("invalid-container");
                if (recordedOrder != null)
                {
                    if (!entity.Contents.Order().SequenceEqual(recordedOrder.Order())) throw new CommandRejected("invalid-shuffle-result");
                    entity.Contents = [..recordedOrder];
                }
                else
                {
                    var random = state.RandomState;
                    for (var i=entity.Contents.Count-1;i>0;i--)
                    {
                        var j=SeededRandom.Index(ref random,i+1);
                        (entity.Contents[i],entity.Contents[j])=(entity.Contents[j],entity.Contents[i]);
                    }
                    state.RandomState=random;
                }
                break;
            case "move":
                if (!double.IsFinite(command.X) || !double.IsFinite(command.Y)) throw new CommandRejected("invalid-position");
                entity.X = command.X; entity.Y = command.Y; break;
            case "flip":
                if (entity.Faces.Count == 0 || command.Value < 0 || command.Value >= entity.Faces.Count) throw new CommandRejected("invalid-face");
                entity.Face = command.Value; entity.FaceUp = !entity.FaceUp; break;
            case "set":
                if (command.Property == null || !entity.Properties.ContainsKey(command.Property)) throw new CommandRejected("invalid-property");
                entity.Properties[command.Property] = command.Value; break;
            case "reveal":
                if (command.Visibility is not ("public" or "owner" or "hidden")) throw new CommandRejected("invalid-visibility");
                if (command.Visibility == "owner" && entity.Owner == null) throw new CommandRejected("invalid-owner");
                entity.Visibility = command.Visibility; entity.FaceUp = command.Visibility == "public"; break;
            case "draw":
                var hand = Access(state, actor, command.Target,ruleEffect);
                if (!entity.IsContainer || !entity.Ordered || entity.Contents.Count == 0 || !hand.IsContainer || hand.Id == entity.Id || hand.Contents.Count >= hand.Capacity)
                    throw new CommandRejected("invalid-container");
                var card = state.Entities[entity.Contents[0]];
                if (card.IsContainer) throw new CommandRejected("invalid-container");
                entity.Contents.RemoveAt(0); hand.Contents.Add(card.Id); card.Container = hand.Id;
                card.Owner = hand.Owner ?? actor; card.Visibility = "owner"; card.FaceUp = false; break;
            case "transfer":
                var target = Access(state, actor, command.Target,ruleEffect);
                if (!target.IsContainer || target.Contents.Count >= target.Capacity || entity.Container == target.Id) throw new CommandRejected("invalid-container");
                for (Entity? parent = target; parent != null; parent = parent.Container == null ? null : state.Entities[parent.Container])
                    if (parent.Id == entity.Id) throw new CommandRejected("invalid-container");
                if (entity.Container != null) Access(state, actor, entity.Container,ruleEffect).Contents.Remove(entity.Id);
                target.Contents.Add(entity.Id); entity.Container = target.Id; break;
            default: throw new CommandRejected("unknown-command");
        }
    }
    private sealed class CommandRejected(string message) : Exception(message);
}
