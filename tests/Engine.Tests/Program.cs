using Engine.Tests;
if(args.Length==2 && args[0]=="--worker-parent-probe") { WorkerTests.ParentProbe(args[1]);return 0; }
var tests = new Dictionary<string, Action> { ["cordi"] = CordiTests.Run, ["freecell"] = FreeCellTests.Run, ["freecell-shortcuts"] = FreeCellShortcutTests.Run, ["freecell-modes"] = FreeCellTests.Modes, ["debug-external"] = DebugTests.External, ["m2-gate"] = M2Acceptance.Run, ["hex"] = HexTests.Run, ["export"] = ExportTests.Run, ["observers"] = DebugTests.Observers, ["timeline"] = DebugTests.Timeline, ["debug-play"] = DebugTests.InProcess, ["reload"] = ReloadTests.Run, ["medium"] = MediumTests.Run, ["layouts"] = AuthorTests.Layouts, ["media"] = AuthorTests.Media, ["prototypes"] = AuthorTests.Prototypes, ["documents"] = AuthorTests.Documents, ["worker"] = WorkerTests.Run, ["latency"] = LatencyTests.Run, ["packages"] = PackageTests.Run, ["interaction"] = InteractionTests.Run, ["view"] = ViewTests.Run, ["modules"] = ModuleTests.Run, ["commands"] = CommandTests.Run, ["visibility"] = VisibilityTests.Run, ["replay"] = ReplayTests.Run, ["turns"] = TurnTests.Run, ["lua"] = LuaTests.Run, ["sandbox"] = SandboxTests.Run, ["cpu"] = CpuTests.Run, ["sample"] = SampleTests.Run, ["batch"] = BatchTests.Run, ["review"] = ReviewTests.Run };
try
{
    tests.Add("online-identity",OnlineTests.Identity);
    tests.Add("online-ordering",OnlineTests.Ordering);
    tests.Add("online-reconnect",OnlineTests.Reconnection);
    tests.Add("online-audience",OnlineTests.Audience);
    tests.Add("online-secrets",OnlineTests.Secrets);
    tests.Add("online-http",OnlineTransportTests.Run);
    tests.Add("online-processes",OnlineTransportTests.Processes);
    tests.Add("online-faults",OnlineTransportTests.Faults);
    tests.Add("runtime-host",RuntimeHostTests.Run);
    tests.Add("review-followup",ReviewFollowupTests.Run);
    tests.Add("startup",StartupTests.Run);
    tests.Add("definition-plugins",DefinitionPluginTests.Run);
    tests.Add("runtime-recording",RuntimeRecordingTests.Run);
    tests.Add("runtime-entry",RuntimeEntryTests.Run);
    tests.Add("alternative-definition",AlternativeDefinitionTests.Run);
    tests.Add("m35-gate",M35Acceptance.Run);
    tests.Add("island",IslandTests.Run);
    tests.Add("island-modes",IslandTests.Modes);
    tests.Add("local-table",LocalTableTests.Run);
    tests.Add("secrets",SecretTests.Run);
    tests.Add("auction",AuctionTests.Run);
    tests.Add("wasm",WasmTests.Run);
    tests.Add("wasm-extensions",WasmTests.Extensions);
    tests.Add("wasm-isolation",WasmTests.Isolation);
    tests.Add("image-import",ImportTests.Images);
    tests.Add("tts-import",ImportTests.Tts);
    tests.Add("print",PrintTests.Run);
    if(args.Any(a=>!tests.ContainsKey(a))) throw new ArgumentException("Unknown test group.");
    foreach (var test in tests.Where(t => (args.Length == 0 && t.Key != "m2-gate" && t.Key != "m35-gate" && t.Key != "export") || args.Contains(t.Key))) { test.Value(); Console.WriteLine($"PASS {test.Key}"); }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
