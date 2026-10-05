namespace BoardGame.Kernel;
public sealed partial class GameEngine
{
    public ReplayLog ExportLog() => Wire.Copy(new ReplayLog(1,EngineVersion.Api,fingerprint,seed,initial,entries));
    private static string EntryHash(Submission submission,List<AppliedCommand> effects,string stateHash,string previousHash) =>
        Wire.Hash(Wire.Json(new { submission,effects,stateHash,previousHash }));
    private void Record(Submission submission,List<AppliedCommand> effects)
    {
        var previous = entries.Count == 0 ? Wire.Hash(Wire.Json(initial)) : entries[^1].Hash;
        var hash = Wire.Hash(Wire.Json(state));
        submissions.Add(submission);
        entries.Add(new(submission,effects,hash,previous,EntryHash(submission,effects,hash,previous)));
    }
    public static GameEngine Replay(LoadedModule module,ReplayLog log)
    {
        try
        {
            if (log.Format != 1 || log.Api != EngineVersion.Api || log.ModuleFingerprint != module.Fingerprint)
                throw new InvalidDataException("Replay format/API/module or script version mismatch.");
            var engine = new GameEngine(module.Initial,log.Seed,module.Fingerprint);
            if (Wire.Json(engine.initial) != Wire.Json(log.Initial)) throw new InvalidDataException("Replay initial state mismatch.");
            var previous=Wire.Hash(Wire.Json(log.Initial));
            foreach (var entry in log.Entries)
            {
                var s=entry.Submission;
                if (s.Sequence != engine.submissions.Count+1L || entry.PreviousHash != previous ||
                    entry.Hash != EntryHash(s,entry.Effects,entry.StateHash,previous)) throw new InvalidDataException("Replay sequence/checksum mismatch.");
                if (!s.Accepted && entry.Effects.Count != 0) throw new InvalidDataException("Rejected command has effects.");
                if (s.Accepted)
                {
                    if (!engine.state.Seats.Any(seat=>seat.Id==s.Actor) || engine.state.Ended || entry.Effects.Count==0) throw new InvalidDataException("Invalid replay actor/effects.");
                    foreach (var effect in entry.Effects)
                    {
                        if ((effect.Command.Kind=="shuffle") != (effect.Order!=null)) throw new InvalidDataException("Missing or unexpected random outcome.");
                        Apply(engine.state,s.Actor,effect.Command,effect.Order);
                        if (effect.RandomAfter==0) throw new InvalidDataException("Invalid random state.");
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
