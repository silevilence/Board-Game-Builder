using BoardGame.Kernel;
namespace BoardGame.Runtime;
public sealed record BatchResult(uint Seed,bool Success,List<string> Winners,int Steps,string? Error,
    string LogPath,string StateHash,bool RerunEqual,bool ReplayEqual);
public static class BatchRunner
{
    public static IReadOnlyList<BatchResult> Run(LoadedModule module,uint startSeed,int count,string output,int maxSteps=500,Action<BatchResult>? report=null)
    {
        if(count<1 || count>10000 || (ulong)startSeed+(uint)count-1>uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
        Directory.CreateDirectory(output);
        var snapshotPath=Path.Combine(output,"module.snapshot.json");
        File.WriteAllText(snapshotPath,Wire.Json(module));
        var results=new List<BatchResult>();
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
        PlaySession Play(uint seed)
        {
            var session=new PlaySession(module,seed,controllers,maxSteps);
            try
            {
                while(!session.Ended && session.Fault==null) session.StepCpu();
                return session;
            }
            catch { session.Dispose();throw; }
        }
        for(var i=0;i<count;i++)
        {
            var seed=startSeed+(uint)i;
            using var session=Play(seed);
            var path=Path.GetFullPath(Path.Combine(output,$"seed-{seed}.json"));
            File.WriteAllText(path,Wire.Json(session.ExportAuthorityLog()));
            File.WriteAllText(path+".diagnostics.json",Wire.Json(new { session.Fault,diagnostics=session.ExportDiagnostics(),module.Fingerprint,seed,maxSteps,snapshotPath }));
            var rerun=false;var replay=false;string? error=session.Fault?.Code;
            try
            {
                var restored=GameEngine.Replay(module,Wire.Read<ReplayLog>(File.ReadAllText(path)));
                replay=Wire.Hash(Wire.Json(restored.ExportAuthorityState()))==session.AuthorityStateHash() &&
                    module.Initial.Seats.All(s=>Wire.Json(restored.Project(s.Id))==Wire.Json(session.View(s.Id)));
                if(session.Ended && session.Fault==null)
                {
                    using var repeated=Play(seed);
                    rerun=repeated.Ended && repeated.Fault==null && Wire.Json(repeated.ExportAuthorityLog())==Wire.Json(session.ExportAuthorityLog());
                }
                if(!replay || (session.Fault==null && !rerun)) error="verification-mismatch";
            }
            catch(Exception e) { error="replay-failure: "+e.Message; }
            var result=new BatchResult(seed,session.Ended && error==null && rerun && replay,session.View(module.Initial.Seats[0].Id).Winners,session.Steps,error,path,session.AuthorityStateHash(),rerun,replay);
            results.Add(result);
            File.WriteAllText(Path.Combine(output,"summary.json"),Wire.Json(results));
            report?.Invoke(result);
        }
        return results;
    }
}
