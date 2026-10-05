using Engine.Tests;
var tests = new Dictionary<string, Action> { ["modules"] = ModuleTests.Run, ["commands"] = CommandTests.Run, ["visibility"] = VisibilityTests.Run };
try
{
    foreach (var test in tests.Where(t => args.Length == 0 || args.Contains(t.Key))) { test.Value(); Console.WriteLine($"PASS {test.Key}"); }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
