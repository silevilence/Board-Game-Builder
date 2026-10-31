using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;
using GamePackage=BoardGame.AppRuntime.GamePackage;

var folder=Path.Combine(Path.GetTempPath(),"board-aot-probe-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var state=new GameState
{
    Seats=[new("p1","one")],Phases=["play"],Phase="play",
    Entities=new(StringComparer.Ordinal)
    {
        ["deck"]=new(){Id="deck",IsContainer=true,Ordered=true,Capacity=3,Contents=["A","a","b"]},
        ["hand"]=new(){Id="hand",IsContainer=true,Owner="p1",Capacity=3,Contents=[]},
        ["A"]=new(){Id="A",Container="deck",Faces=["A"]},
        ["a"]=new(){Id="a",Container="deck",Faces=["a"]},
        ["b"]=new(){Id="b",Container="deck",Faces=["b"]}
    }
};
File.WriteAllText(Path.Combine(folder,"state.json"),JsonSerializer.Serialize(new ProbeStateFile(1,state),ProbeJsonContext.Default.ProbeStateFile));
RuntimePluginSelection P(string id,string configuration="{}")
{using var json=JsonDocument.Parse(configuration);return new(id,"1",1,json.RootElement.Clone());}
var startup=new RuntimeStartup(1,"aot-board-probe","1",
[
    P("definition.board-state","""{"path":"state.json"}"""),P("resources"),P("kernel"),P("session.local"),P("recording"),
    P("rules.commands","""{"actions":[{"id":"shuffle","label":"Shuffle","command":{"kind":"shuffle","entity":"deck"}},{"id":"draw","label":"Draw","command":{"kind":"draw","entity":"deck","target":"hand"}},{"id":"end","label":"End","command":{"kind":"finish","winners":["p1"]}}]}""")
]);
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),JsonSerializer.Serialize(startup,ProbeJsonContext.Default.RuntimeStartup));
var board=new BoardApplication(42);var approved=new ApplicationCatalog(board.Plugins());
var content=GameRuntime.Load(folder,approved.Content);
GamePackage.Pack(folder,folder+".bgm",approved.Content);
var packaged=GameRuntime.Load(folder+".bgm",approved.Content);
Check(content.Fingerprint==packaged.Fingerprint,"package content differs");
var results=new List<string>();
for(var repeat=0;repeat<2;repeat++)
{
    await using var host=await approved.StartAsync(packaged);
    var session=host.Service<IGameSession>("session");
    GameEngine? resumed=null;
    foreach(var actionId in new[]{"shuffle","draw","end"})
    {
        var command=session.Actions().Single(a=>a.Id==actionId).Command;
        var request=new BoardUpdateRequest(session.ExportAuthorityLog().EntryCount,"p1",command);
        host.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(request,ProbeJsonContext.Default.BoardUpdateRequest));
        await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
        Check(session.ExportAuthorityLog().Entries[^1].Submission.Accepted,"command rejected: "+actionId);
        if(actionId=="draw")
        {
            var checkpoint=Wire.Read<SessionRecording>(Wire.Json(session.ExportRecording()));
            resumed=checkpoint.Replay(module=>board.Validate(packaged,module));
        }
    }
    Check(session.Ended && session.Steps==3,"update did not finish three commands");
    Check(resumed!=null && resumed.Submit("p1",new(){Kind="finish",Winners=["p1"]}).Accepted,"checkpoint could not resume");
    Check(Wire.Json(resumed!.ExportAuthorityState())==Wire.Json(session.ExportAuthorityState()),"resumed checkpoint state differs");
    var recording=session.ExportRecording();
    var json=Wire.Json(recording);
    var restored=Wire.Read<SessionRecording>(json);
    var replay=restored.Replay(module=>board.Validate(packaged,module));
    Check(Wire.Json(replay.Project("p1"))==Wire.Json(session.View("p1")),"restored replay projection differs");
    Check(Wire.Json(replay.ExportAuthorityState())==Wire.Json(session.ExportAuthorityState()),"restored replay authority differs");
    var hash=Wire.Hash(json);
    results.Add(hash);
    Console.WriteLine($"PASS repeat={repeat}; recording={hash}; state={session.AuthorityStateHash()}; drawn={session.ExportAuthorityState().Entities["hand"].Contents.Single()}");
    var changed=restored with{Hash="invalid"};
    var rejected=false;try{changed.Replay(module=>board.Validate(packaged,module));}catch(InvalidDataException){rejected=true;}
    Check(rejected,"tampered recording was accepted");
}
Check(results[0]==results[1],"fixed seed recording differs");
var copied=Wire.Copy(state);
Check(copied.Entities.Comparer.Equals(StringComparer.Ordinal),"ordinal comparer lost");
Check(copied.Entities.Keys.Take(2).SequenceEqual(new[]{"A","a"}),"mixed-case ordinal ordering changed");
Console.WriteLine("PASS package identity, seeded shuffle/draw/finish update, recording restore/resume, tamper rejection, ordinal dictionaries");
return 0;

static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
internal sealed record ProbeStateFile(int Format,GameState State);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProbeStateFile))]
[JsonSerializable(typeof(RuntimeStartup))]
[JsonSerializable(typeof(BoardUpdateRequest))]
internal partial class ProbeJsonContext:JsonSerializerContext;
