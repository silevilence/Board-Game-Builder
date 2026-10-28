namespace BoardGame.Kernel;

public sealed partial class GameEngine
{
    // Private history entries and the initial state are never mutated after insertion;
    // public exports are detached copies. A branch copies only the mutable working state.
    private GameEngine(GameEngine source)
    {
        state=Wire.Copy(source.state);initial=source.initial;fingerprint=source.fingerprint;
        seed=source.seed;rules=source.rules;
        submissions.AddRange(source.submissions);entries.AddRange(source.entries);diagnostics.AddRange(source.diagnostics);
    }
    public GameEngine Fork()=>new(this);
    public long Revision=>submissions.Count;
}
