using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ApplicationBaselineTests
{
    private const string PathName="docs/validation/application-baseline.json";
    public sealed record Outcome(string Sample,uint Seed,string Content,string Log,string State,string Projections,string? Flow,int Steps);
    public static void Capture()=>File.WriteAllText(PathName,Wire.Json(Measure()));
    public static void Run()
    {
        var expected=Wire.Read<List<Outcome>>(File.ReadAllText(PathName));
        var actual=Measure();
        Check.That(expected.Count==actual.Count,"Baseline scenario count changed");
        for(var i=0;i<actual.Count;i++) Check.That(expected[i]==actual[i],$"Baseline differs: {actual[i].Sample}/{actual[i].Seed}\n{Wire.Json(expected[i])}\n{Wire.Json(actual[i])}");
    }
    private static List<Outcome> Measure()
    {
        List<Outcome> outcomes=[];
        foreach(var sample in new[]{"quick-cards","hex-skirmish","island-settlers","sealed-auction","poker-uno","flow-cards","wasm-counter"})
        foreach(uint seed in new uint[]{1,7,42})
        {
            // Preserve the original content identity. Explicit entry migration is verified separately.
            var module=ApplicationMigrationTests.Legacy(sample);
            var hasCpu=module.RuntimeIdentity!.Startup.Plugins.Any(p=>p.Id=="lua.cpu");
            using var session=new PlaySession(module,seed,module.Initial.Seats.ToDictionary(s=>s.Id,_=>hasCpu?"cpu":"human"));
            List<string> projections=[];
            void Observe()=>projections.Add(Wire.Hash(Wire.Json(module.Initial.Seats.Select(s=>session.View(s.Id)).ToArray())));
            Observe();
            while(!session.Ended && session.Fault==null && session.Steps<500)
            {
                if(session.IsCpu) Check.That(session.StepCpu());
                else Check.That(session.SubmitHuman(session.CurrentSeat,session.Actions()[0].Command).Accepted);
                Observe();
            }
            Check.That(session.Ended && session.Fault==null,sample+": "+Wire.Json(session.Fault));
            var state=session.AuthorityStateHash();
            var replay=session.ExportRecording().Replay();
            Check.That(state==Wire.Hash(Wire.Json(replay.ExportAuthorityState())),sample+": replay state differs");
            foreach(var seat in module.Initial.Seats) Check.That(Wire.Json(session.View(seat.Id))==Wire.Json(replay.Project(seat.Id)));
            outcomes.Add(new(sample,seed,module.Fingerprint,Wire.Hash(Wire.Json(session.ExportAuthorityLog())),state,
                Wire.Hash(Wire.Json(projections)),session.ExportAuthorityState().Flow==null?null:Wire.Hash(Wire.Json(session.ExportAuthorityState().Flow)),session.Steps));
            Console.WriteLine($"Baseline {sample}/{seed}: {session.Steps} steps, {state}");
        }
        return outcomes;
    }
}
