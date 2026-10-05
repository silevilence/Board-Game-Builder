using Engine.Tests;
var tests = new Dictionary<string, Action> { ["interaction"] = InteractionTests.Run, ["view"] = ViewTests.Run, ["modules"] = ModuleTests.Run, ["commands"] = CommandTests.Run, ["visibility"] = VisibilityTests.Run, ["replay"] = ReplayTests.Run, ["turns"] = TurnTests.Run, ["lua"] = LuaTests.Run, ["sandbox"] = SandboxTests.Run, ["cpu"] = CpuTests.Run, ["sample"] = SampleTests.Run, ["batch"] = BatchTests.Run, ["review"] = ReviewTests.Run };
try
{
    if(args.Any(a=>!tests.ContainsKey(a))) throw new ArgumentException("Unknown test group.");
    foreach (var test in tests.Where(t => args.Length == 0 || args.Contains(t.Key))) { test.Value(); Console.WriteLine($"PASS {test.Key}"); }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
