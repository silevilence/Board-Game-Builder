using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class SamplesReleaseTests
{
    public static void Run()
    {
        var output=Path.GetFullPath(Environment.GetEnvironmentVariable("BGB_SAMPLES_REPORT") ?? ".scratch/release-v0.4.0/samples-headless.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var results=new List<object>();
        foreach(var entry in Directory.GetFiles("samples","game.start.json",SearchOption.AllDirectories).Order())
        {
            using var json=JsonDocument.Parse(File.ReadAllText(entry));
            if(!json.RootElement.GetProperty("plugins").EnumerateArray().Any(p=>p.GetProperty("id").GetString() is "definition.module" or "definition.state"))continue;
            var source=Path.GetDirectoryName(entry)!;var name=Path.GetFileName(source);
            var package=Path.Combine(Path.GetDirectoryName(output)!,name+".bgm");
            GamePackage.Pack(source,package);
            var initial=GameRuntime.Load(source);
            var presets=GamePresets.Choices(initial).Keys.Cast<string?>().ToArray();
            if(presets.Length==0)presets=[null];
            foreach(var preset in presets)
            {
                var module=GameRuntime.Load(source,preset);var packed=GameRuntime.Load(package,preset);
                Check.That(module.Fingerprint==packed.Fingerprint,name+": package identity changed");
                var first=Play(module,name);var second=Play(packed,name);
                Check.That(first==second,name+": directory/package result differs");
                results.Add(new{sample=name,preset,source,package,module.Fingerprint,steps=first.Steps,stateHash=first.Hash,ended=true,replayEqual=true});
                Console.WriteLine($"PASS sample {name}/{preset ?? "default"}: {first.Steps} steps; directory, package and replay agree");
            }
        }
        Check.That(results.Count>=11,"Sample inventory unexpectedly empty");
        File.WriteAllText(output,Wire.Json(new{success=true,version=typeof(PlaySession).Assembly.GetName().Version!.ToString(),results}));
    }
    private static (int Steps,string Hash) Play(LoadedModule module,string name)
    {
        var solution=name=="freecell"?Wire.Read<List<Command>>(File.ReadAllText("samples/freecell/solution-seed-1.json")):null;
        var cpu=solution==null && module.RuntimeIdentity!.Startup.Plugins.Any(p=>p.Id=="lua.cpu");
        using var game=new PlaySession(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,_=>cpu?"cpu":"human"),maxSteps:2400);
        var next=0;
        while(!game.Ended && game.Fault==null && game.Steps<2400)
        {
            if(cpu)Check.That(game.StepCpu(),name+": CPU did not advance");
            else
            {
                var offered=game.Actions();Check.That(offered.Count>0,name+": no legal action");
                var command=solution==null?offered[0].Command:solution[next++];
                Check.That(offered.Any(a=>Wire.Json(a.Command)==Wire.Json(command)),name+": solution action not offered");
                Check.That(game.SubmitHuman(game.CurrentSeat,command).Accepted,name+": action rejected");
            }
        }
        Check.That(game.Ended && game.Fault==null,name+": "+Wire.Json(game.Fault));
        var replay=game.ExportRecording().Replay();var hash=game.AuthorityStateHash();
        Check.That(hash==Wire.Hash(Wire.Json(replay.ExportAuthorityState())),name+": replay changed state");
        foreach(var seat in module.Initial.Seats)Check.That(Wire.Json(game.View(seat.Id))==Wire.Json(replay.Project(seat.Id)),name+": replay changed projection");
        return(game.Steps,hash);
    }
}
