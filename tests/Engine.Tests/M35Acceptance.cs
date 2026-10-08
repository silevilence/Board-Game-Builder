using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class M35Acceptance
{
    public static void Run()
    {
        var root=Path.GetFullPath("artifacts/m3.5/gate");Directory.CreateDirectory(root);
        foreach(var name in new[]{"quick-cards","hex-skirmish","freecell","sealed-auction","wasm-counter","island-settlers","state-minimal"})
        {
            var module=GameRuntime.Load("samples/"+name);var package=Path.Combine(root,name+"-"+Guid.NewGuid()+".bgm");
            GamePackage.Pack("samples/"+name,package);var packed=GameRuntime.Load(package);
            Check.That(module.Fingerprint==packed.Fingerprint && Wire.Json(module.Initial)==Wire.Json(packed.Initial),name+": directory/package mismatch");
            if(name is "quick-cards" or "hex-skirmish" or "island-settlers" or "sealed-auction")
            {
                var results=BatchRunner.Run(module,1,20,Path.Combine(root,name),report:r=>Console.WriteLine($"M3.5 {name}/{r.Seed}: {r.Success}, steps={r.Steps}"));
                Check.That(results.All(r=>r.Success),name+": deterministic batch gate failed");
                using var session=new PlaySession(packed,1,packed.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
                while(!session.Ended && session.Fault==null) session.StepCpu();
                Check.That(session.Ended && session.AuthorityStateHash()==results[0].StateHash,name+": package game differed");
                Check.That(session.ExportRecording().Format==2 && Wire.Hash(Wire.Json(session.ExportRecording().Replay().ExportAuthorityState()))==session.AuthorityStateHash());
            }
            else
            {
                using var session=new PlaySession(packed,1);
                var commands=name=="freecell"?Wire.Read<List<Command>>(File.ReadAllText("samples/freecell/solution-seed-1.json")):null;
                if(commands!=null) foreach(var command in commands) Check.That(session.SubmitHuman("p1",command).Accepted);
                else for(var step=0;step<10 && !session.Ended;step++) Check.That(session.SubmitHuman(session.CurrentSeat,session.Actions().First().Command).Accepted);
                Check.That(session.Ended && session.Fault==null,name+": package did not finish");
                Check.That(Wire.Hash(Wire.Json(session.ExportRecording().Replay().ExportAuthorityState()))==session.AuthorityStateHash());
                File.WriteAllText(Path.Combine(root,name+".json"),Wire.Json(new {success=true,session.Steps,module.Fingerprint,replayEqual=true}));
            }
        }
    }
}
