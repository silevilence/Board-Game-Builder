namespace BoardGame.Kernel;
public sealed record AppliedCommand(Command Command, bool RuleEffect, List<string>? Order, uint RandomAfter);
public sealed record LogEntry(Submission Submission, List<AppliedCommand> Effects, string StateHash, string PreviousHash, string Hash);
public sealed record ReplayLog(int Format, int Api, string ModuleFingerprint, uint Seed, GameState Initial, List<LogEntry> Entries, int EntryCount, string FinalHash, string HeaderHash);
public static class SeededRandom
{
    public static uint Next(ref uint state)
    {
        if (state == 0) state = 0x9E3779B9;
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return state;
    }
    public static int Index(ref uint state, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return (int)(Next(ref state) % (uint)count);
    }
}
