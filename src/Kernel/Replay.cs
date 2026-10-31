namespace BoardGame.Kernel;
public sealed partial class GameEngine
{
    public ReplayLog ExportLog() => Wire.Copy(new ReplayLog(1,EngineVersion.Api,fingerprint,seed,initial,entries,entries.Count,entries.Count==0?Wire.Hash(Wire.Json(initial)):entries[^1].Hash,HeaderHash(fingerprint,seed,initial)));
    private static string HeaderHash(string fingerprint,uint seed,GameState initial) => Wire.Hash(Wire.Json(new ReplayHeaderInput(fingerprint,seed,initial),Wire.Type<ReplayHeaderInput>(ReplayJson.Default)));
    private static string EntryHash(Submission submission,List<AppliedCommand> effects,string stateHash,string previousHash) =>
        Wire.Hash(Wire.Json(new ReplayEntryInput(submission,effects,stateHash,previousHash),Wire.Type<ReplayEntryInput>(ReplayJson.Default)));
    private void Record(Submission submission,List<AppliedCommand> effects,GameState? candidate=null)
    {
        var previous = entries.Count == 0 ? Wire.Hash(Wire.Json(initial)) : entries[^1].Hash;
        var hash = Wire.Hash(Wire.Json(candidate ?? state));
        var entry = new LogEntry(submission,effects,hash,previous,EntryHash(submission,effects,hash,previous));
        submissions.Add(submission);
        entries.Add(entry);
        if(candidate!=null) state=candidate;
    }
    public static GameEngine Replay(LoadedModule module,ReplayLog log)
    {
        try
        {
            if (log.Format != 1 || log.Api != EngineVersion.Api || log.ModuleFingerprint != module.Fingerprint)
                throw new InvalidDataException("Replay format/API/module or script version mismatch.");
            var engine = new GameEngine(module.Initial,log.Seed,module.Fingerprint);
            if (Wire.Json(engine.initial) != Wire.Json(log.Initial)) throw new InvalidDataException("Replay initial state mismatch.");
            if(log.HeaderHash!=HeaderHash(log.ModuleFingerprint,log.Seed,log.Initial)) throw new InvalidDataException("Replay header checksum mismatch.");
            if(log.EntryCount!=log.Entries.Count || log.FinalHash!=(log.Entries.Count==0?Wire.Hash(Wire.Json(log.Initial)):log.Entries[^1].Hash)) throw new InvalidDataException("Replay footer/count mismatch.");
            var previous=Wire.Hash(Wire.Json(log.Initial));
            foreach (var entry in log.Entries)
            {
                var s=entry.Submission;
                if (s.Sequence != engine.submissions.Count+1L || entry.PreviousHash != previous ||
                    entry.Hash != EntryHash(s,entry.Effects,entry.StateHash,previous)) throw new InvalidDataException("Replay sequence/checksum mismatch.");
                if (!s.Accepted && entry.Effects.Count != 0) throw new InvalidDataException("Rejected command has effects.");
                if (s.Accepted)
                {
                    var flow=engine.state.Flow;
                    if(flow==null ? s.Execution!=null || !CanAct(engine.state,s.Actor) || entry.Effects.Count is <1 or >64 :
                        s.Execution is not ("system" or "player") || entry.Effects.Count is <1 or >66 ||
                        (s.Execution=="system" ? s.Actor!="" || flow.Status!="ready" : !CanAct(engine.state,s.Actor)) ||
                        entry.Effects[^1].Flow==null || entry.Effects.Take(entry.Effects.Count-1).Any(e=>e.Flow!=null))
                        throw new InvalidDataException("Invalid replay actor/effects.");
                    var submittedInBatch=false;
                    foreach (var effect in entry.Effects)
                    {
                        if(effect.Flow is {} continuation)
                        {
                            if(flow==null || effect.Command.Kind!="flow-state" || continuation.Revision!=flow.Revision+1 || continuation.DefinitionHash!=flow.DefinitionHash || effect.RandomAfter!=engine.state.RandomState || effect.Order!=null || effect.Roll!=null)
                                throw new InvalidDataException("Invalid replay flow continuation");
                            engine.state.Flow=Wire.Copy(continuation);StateValidation.Validate(engine.state);continue;
                        }
                        if ((effect.Command.Kind=="shuffle") != (effect.Order!=null)) throw new InvalidDataException("Missing or unexpected random outcome.");
                        if((effect.Command.Kind=="roll")!=(effect.Roll!=null)) throw new InvalidDataException("Missing or unexpected dice outcome.");
                        Apply(engine.state,s.Actor,effect.Command,effect.Order,effect.RuleEffect,effect.Roll,submittedInBatch,system:s.Execution=="system");
                        submittedInBatch|=effect.Command.Kind=="secret-submit";
                        if (effect.RandomAfter==0 || (effect.Command.Kind is not ("shuffle" or "roll") && effect.RandomAfter!=engine.state.RandomState)) throw new InvalidDataException("Invalid random state.");
                        engine.state.RandomState=effect.RandomAfter;
                    }
                }
                if (Wire.Hash(Wire.Json(engine.state)) != entry.StateHash) throw new InvalidDataException("Replay state mismatch.");
                engine.submissions.Add(Wire.Copy(s)); engine.entries.Add(Wire.Copy(entry)); previous=entry.Hash;
            }
            return engine;
        }
        catch (Exception e) when (e is CommandRejected or NullReferenceException or ArgumentException or KeyNotFoundException)
        { throw new InvalidDataException("Corrupt replay: " + e.Message,e); }
    }
}
