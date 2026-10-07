using Engine.Tests;
if(args.Length==2 && args[0]=="--worker-parent-probe") { WorkerTests.ParentProbe(args[1]);return 0; }
var tests = new Dictionary<string, Action> { ["cordi"] = CordiTests.Run, ["freecell"] = FreeCellTests.Run, ["freecell-shortcuts"] = FreeCellShortcutTests.Run, ["freecell-modes"] = FreeCellTests.Modes, ["debug-external"] = DebugTests.External, ["m2-gate"] = M2Acceptance.Run, ["hex"] = HexTests.Run, ["export"] = ExportTests.Run, ["observers"] = DebugTests.Observers, ["timeline"] = DebugTests.Timeline, ["debug-play"] = DebugTests.InProcess, ["reload"] = ReloadTests.Run, ["medium"] = MediumTests.Run, ["layouts"] = AuthorTests.Layouts, ["media"] = AuthorTests.Media, ["prototypes"] = AuthorTests.Prototypes, ["documents"] = AuthorTests.Documents, ["worker"] = WorkerTests.Run, ["latency"] = LatencyTests.Run, ["packages"] = PackageTests.Run, ["interaction"] = InteractionTests.Run, ["view"] = ViewTests.Run, ["modules"] = ModuleTests.Run, ["commands"] = CommandTests.Run, ["visibility"] = VisibilityTests.Run, ["replay"] = ReplayTests.Run, ["turns"] = TurnTests.Run, ["lua"] = LuaTests.Run, ["sandbox"] = SandboxTests.Run, ["cpu"] = CpuTests.Run, ["sample"] = SampleTests.Run, ["batch"] = BatchTests.Run, ["review"] = ReviewTests.Run };
try
{
    tests.Add("local-table",LocalTableTests.Run);
    tests.Add("secrets",SecretTests.Run);
    tests.Add("auction",AuctionTests.Run);
    if(args.Any(a=>!tests.ContainsKey(a))) throw new ArgumentException("Unknown test group.");
    foreach (var test in tests.Where(t => (args.Length == 0 && t.Key != "m2-gate" && t.Key != "export") || args.Contains(t.Key))) { test.Value(); Console.WriteLine($"PASS {test.Key}"); }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
