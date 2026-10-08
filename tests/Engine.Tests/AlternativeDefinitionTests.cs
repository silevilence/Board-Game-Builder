using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class AlternativeDefinitionTests
{
    public static void Convert(string directory)
    {
        var module=GameRuntime.Load(directory);var old=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"module.json")))!;
        var state=new JsonObject {["format"]=1,["state"]=JsonSerializer.SerializeToNode(module.Initial,Wire.Options),["resources"]=old["resources"]!.DeepClone()};
        File.WriteAllText(Path.Combine(directory,"position.json"),state.ToJsonString());
        var file=Path.Combine(directory,GameRuntime.Entry);var startup=JsonNode.Parse(File.ReadAllText(file))!;
        var loader=startup["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="definition.module")!;
        loader["id"]="definition.state";loader["configuration"]!["path"]="position.json";
        File.WriteAllText(file,startup.ToJsonString());
    }
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-alternative-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/quick-cards",directory);var original=GameRuntime.Load(directory);
            Convert(directory);var alternative=GameRuntime.Load(directory);
            Check.That(Wire.Json(original.Initial)==Wire.Json(alternative.Initial));
            var controllers=original.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
            using var first=new PlaySession(original,42,controllers);using var second=new PlaySession(alternative,42,controllers);
            while(!first.Ended && first.Fault==null) {Check.That(first.StepCpu()==second.StepCpu());Check.That(first.AuthorityStateHash()==second.AuthorityStateHash());}
            Check.That(first.Ended && second.Ended && second.Fault==null);
            Check.That(Wire.Json(first.ExportRecording().Replay().ExportAuthorityState())==Wire.Json(second.ExportRecording().Replay().ExportAuthorityState()));
            var package=Path.Combine(directory,"alternative.bgm");GamePackage.Pack(directory,package);Check.That(GameRuntime.Load(package).Fingerprint==alternative.Fingerprint);
            var file=Path.Combine(directory,GameRuntime.Entry);var startup=JsonNode.Parse(File.ReadAllText(file))!;
            startup["plugins"]!.AsArray().Add(JsonNode.Parse("""{"id":"definition.module","version":"1","api":1,"configuration":{"path":"module.json"}}"""));
            File.WriteAllText(file,startup.ToJsonString());Check.Throws(()=>GameRuntime.Load(directory),"duplicate/invalid capability definition");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
        var minimal=GameRuntime.Load("samples/state-minimal");using var game=new PlaySession(minimal);
        Check.That(game.SubmitHuman("p1",game.Actions().Single().Command).Accepted && game.Ended);
    }
}
