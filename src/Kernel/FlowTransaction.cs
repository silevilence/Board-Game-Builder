namespace BoardGame.Kernel;

public sealed partial class GameEngine
{
    // Only trusted authority plugins receive this capability. Player commands cannot call it.
    public CommandResult SubmitFlow(string? actor,Command command,long revision,Action<FlowTransaction> operation)
    {
        var sequence=submissions.Count+1L;var identity=actor==null?"system":"player";
        try
        {
            var current=state.Flow ?? throw new CommandRejected("flow-not-enabled");
            if(state.Ended || current.Revision!=revision || (actor==null ? current.Status!="ready" : current.Status!="waiting" || current.Seat!=actor))
                throw new CommandRejected("flow-stale-or-unauthorized");
            var transaction=new FlowTransaction(Wire.Copy(state),actor);
            operation(transaction);
            var candidate=transaction.candidate;
            if(candidate.Flow==null || candidate.Flow.Revision!=revision+1 || candidate.Flow.DefinitionHash!=current.DefinitionHash)
                throw new CommandRejected("flow-invalid-continuation");
            StateValidation.Validate(candidate);
            transaction.effects.Add(new(new(){Kind="flow-state"},true,null,candidate.RandomState,Flow:Wire.Copy(candidate.Flow)));
            Record(new(sequence,actor ?? "",Wire.Copy(command),true,"accepted",identity),transaction.effects,candidate);
            return new(sequence,true,"accepted");
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            // Script exceptions may contain authority data. Never record their text in public reasons.
            var reason=error is CommandRejected?error.Message:"flow-execution-failed";
            diagnostics.Add(new(sequence,actor ?? "","flow",reason));
            Record(new(sequence,actor ?? "",Wire.Copy(command),false,reason,identity),[]);
            return new(sequence,false,reason);
        }
    }
    public sealed class FlowTransaction
    {
        internal readonly GameState candidate;
        internal readonly List<AppliedCommand> effects=[];
        private readonly string? actor;
        internal FlowTransaction(GameState state,string? actor) {candidate=state;this.actor=actor;}
        public GameState Snapshot()=>Wire.Copy(candidate);
        public void Apply(Command command)
        {
            if(effects.Count>=64 || command.Kind is "flow-state" or "finish" || command.Kind.StartsWith("secret-",StringComparison.Ordinal))
                throw new InvalidDataException("flow-effect: unsupported command or effect budget exceeded");
            effects.Add(Execute(candidate,actor ?? "",command,true,system:actor==null));
        }
        public void Complete(FlowState continuation,List<string>? winners=null)
        {
            if(winners!=null) effects.Add(Execute(candidate,actor ?? "",new(){Kind="finish",Winners=winners},true,system:actor==null));
            candidate.Flow=Wire.Copy(continuation);
        }
    }
}
