namespace BoardGame.Kernel;
public sealed record AppliedCommand(Command Command, bool RuleEffect, List<string>? Order, uint RandomAfter,int? Roll=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] FlowState? Flow=null);
public sealed record LogEntry(Submission Submission, List<AppliedCommand> Effects, string StateHash, string PreviousHash, string Hash);
public sealed record ReplayLog(int Format, int Api, string ModuleFingerprint, uint Seed, GameState Initial, List<LogEntry> Entries, int EntryCount, string FinalHash, string HeaderHash);
