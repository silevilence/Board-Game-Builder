using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class ApplicationRegressionTests
{
    private sealed record Result(uint Seed,int Steps,string State,string Actions,string Projections,string? Flow);
    public static void Run()
    {
        var evidence=new List<object>();
        foreach(var sample in new[]{"quick-cards","hex-skirmish","island-settlers","sealed-auction","poker-uno","flow-cards","wasm-counter"})
        {
            var original=ApplicationMigrationTests.Legacy(sample);var current=GameRuntime.Load("samples/"+sample);
            var seeds=sample is "quick-cards" or "hex-skirmish" or "island-settlers"?Enumerable.Range(1,20).Select(s=>(uint)s):new uint[]{1,7,42};
            foreach(var seed in seeds)
            {
                var before=Measure(original,seed,false);var after=Measure(current,seed,true);
                Check.That(before==after,sample+"/"+seed+": semantic migration regression\n"+Wire.Json(new{before,after}));
                evidence.Add(new{sample,seed,legacyFingerprint=original.Fingerprint,fingerprint=current.Fingerprint,result=after});
                Console.WriteLine($"Semantic migration {sample}/{seed}: {after.Steps} steps; replay, actions and projections match");
            }
        }
        var report=Environment.GetEnvironmentVariable("BGB_REGRESSION_REPORT");
        if(report!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);File.WriteAllText(report,Wire.Json(new{success=true,scenarios=evidence}));}
    }
    private static Result Measure(LoadedModule module,uint seed,bool repeatedReads)
    {
        var hasCpu=module.RuntimeIdentity!.Startup.Plugins.Any(p=>p.Id=="lua.cpu");
        using var game=new PlaySession(module,seed,module.Initial.Seats.ToDictionary(s=>s.Id,_=>hasCpu?"cpu":"human"));
        var projections=new List<string>();
        void Observe()
        {
            projections.Add(Wire.Hash(Wire.Json(module.Initial.Seats.Select(s=>game.View(s.Id)))));
            if(repeatedReads)
            {
                var revision=game.Steps;var state=game.AuthorityStateHash();
                for(var frame=0;frame<4;frame++)foreach(var seat in module.Initial.Seats)_=game.View(seat.Id);
                Check.That(game.Steps==revision && game.AuthorityStateHash()==state,"Read/render cadence advanced authority");
            }
        }
        Observe();
        while(!game.Ended && game.Fault==null && game.Steps<500)
        {
            if(game.IsCpu)Check.That(game.StepCpu());else Check.That(game.SubmitHuman(game.CurrentSeat,game.Actions()[0].Command).Accepted);
            Observe();
        }
        Check.That(game.Ended && game.Fault==null,module.Id+": "+Wire.Json(game.Fault));
        var state=game.AuthorityStateHash();var replay=game.ExportRecording().Replay();
        Check.That(state==Wire.Hash(Wire.Json(replay.ExportAuthorityState())),"Replay changed authority");
        foreach(var seat in module.Initial.Seats)Check.That(Wire.Json(game.View(seat.Id))==Wire.Json(replay.Project(seat.Id)),"Replay changed seat projection");
        var log=game.ExportAuthorityLog();
        // Each complete hash chain has already been checked by Replay. The renderer declaration changes
        // content identity/header hashes, so compare submitted actions, applied effects and state hashes.
        var actions=Wire.Hash(Wire.Json(log.Entries.Select(e=>new{e.Submission,e.Effects,e.StateHash})));
        var flow=game.ExportAuthorityState().Flow;
        return new(seed,game.Steps,state,actions,Wire.Hash(Wire.Json(projections)),flow==null?null:Wire.Hash(Wire.Json(flow)));
    }
}
