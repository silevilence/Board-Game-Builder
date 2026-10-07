using BoardGame.Kernel;
using BoardGame.Lua;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace Engine.Tests;

public static class FreeCellTests
{
    private const string Source="samples/freecell";
    private static Command Place(string card,string target)=>new() {Kind="place",Entity=card,Target=target};
    public static void Run()
    {
        var module=DirectoryModule.Load(Source);
        Check.That(module.ResourceData!.Count==53 && module.ResourceData.ContainsKey("player.presentation") && module.Initial.Seats.Count==1);
        Check.That(ModuleSnapshot.Fingerprint(Wire.Copy(module))==module.Fingerprint,"Mixed-case runtime paths changed fingerprint at wire boundary");
        var output=Path.GetFullPath("artifacts/freecell-validation"); Directory.CreateDirectory(output);
        using var worker=new LuaWorkerSession();
        GameEngine Engine(GameState? state=null,uint seed=42)=>new(state ?? module.Initial,seed,module.Fingerprint,new LuaRules(module.Rules,module.RulesPath,seed,worker));
        void Accept(GameEngine engine,Command command) { var r=engine.Submit("p1",command); Check.That(r.Accepted,Wire.Json(new {r,diagnostics=engine.ExportDiagnostics()})); }
        void Reject(GameEngine engine,Command command)
        {
            var before=Wire.Json(engine.ExportAuthorityState());
            Check.That(!engine.Submit("p1",command).Accepted,"Illegal FreeCell command accepted");
            Check.That(Wire.Json(engine.ExportAuthorityState())==before,"Rejected command mutated state");
        }
        var summaries=new List<object>();
        for(uint seed=1;seed<=20;seed++)
        {
            var engine=Engine(seed:seed); var repeat=Engine(seed:seed);
            foreach(var e in new[]{engine,repeat}) { Accept(e,new(){Kind="start"}); Accept(e,new(){Kind="deal"}); }
            Check.That(Wire.Json(engine.ExportAuthorityState())==Wire.Json(repeat.ExportAuthorityState()),"Seed mismatch");
            var dealt=engine.ExportAuthorityState();
            for(var i=1;i<=8;i++) Check.That(dealt.Entities["column-"+i].Contents.Count==(i<=4?7:6));
            Check.That(dealt.Entities.Values.Where(e=>e.Properties.ContainsKey("rank")).All(e=>e.FaceUp && e.Visibility=="public"));
            Check.That(dealt.Entities.Values.SelectMany(e=>e.Contents).Distinct().Count()==52);
            var scene=TableScene.From(engine.Project("p1"),module.ResourceData);
            Check.That(scene.Items.Count(i=>i.Kind=="card")==52 && scene.Resources.Count==52);
            Check.That(!scene.Items.Any(i=>i.Entity is "deck" or "match"));
            foreach(var column in dealt.Entities.Values.Where(e=>e.Id.StartsWith("column-")))
            {
                var cards=scene.Items.Where(i=>column.Contents.Contains(i.Entity!)).ToArray();
                Check.That(cards.Select(c=>c.Entity).SequenceEqual(column.Contents),"Rendering reordered the pile");
                Check.That(scene.Hit(cards[0].Bounds.X+5,cards[0].Bounds.Y+5)?.Entity==column.Contents[0]);
                Check.That(scene.Region(cards[^1].Bounds.X+5,cards[^1].Bounds.Y+100)?.Entity==column.Id);
            }
            var first=engine.Actions("p1").First(a=>a.Command.Kind=="place");
            var resolved=PlayerSession.Resolve(engine.Actions("p1"),new("drag",first.Command.Entity,first.Target));
            Check.That(Wire.Json(resolved)==Wire.Json(first.Command));
            Accept(engine,resolved); Accept(engine,new(){Kind="undo"});
            var undone=engine.ExportAuthorityState();
            foreach(var e in dealt.Entities.Values.Where(e=>e.IsContainer)) Check.That(e.Contents.SequenceEqual(undone.Entities[e.Id].Contents),"Undo reordered cards");
            Reject(engine,new(){Kind="undo"}); Reject(engine,new(){Kind="transfer",Entity=first.Command.Entity,Target=first.Target});
            Check.That(Wire.Json(GameEngine.Replay(module,engine.ExportLog()).ExportAuthorityState())==Wire.Json(engine.ExportAuthorityState()));
            File.WriteAllText(Path.Combine(output,$"seed-{seed}.json"),Wire.Json(engine.ExportLog()));
            summaries.Add(new {seed,rerun=true,replay=true,cards=52,undo=true,solved=false});
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),Wire.Json(summaries));
        // Fixtures keep all 52 cards. Unused cards stay in the deck, outside the tested move.
        GameState Board(params (string Zone,string[] Cards)[] zones)
        {
            var state=Wire.Copy(module.Initial); state.Phase="play";
            foreach(var (zone,cards) in zones)
                foreach(var id in cards)
                {
                    var c=state.Entities[id]; state.Entities[c.Container!].Contents.Remove(id);
                    state.Entities[zone].Contents.Add(id); c.Container=zone;
                }
            state.Entities["match"].Properties["home"]=state.Entities.Values.Where(e=>e.Id.StartsWith("home-")).Sum(e=>e.Contents.Count);
            return state;
        }
        var basic=Engine(Board(("column-1",["card-07","card-19","card-05"]),("column-2",["card-34"]),("cell-1",["card-01"])));
        Reject(basic,Place("card-07","cell-2"));
        Reject(basic,Place("card-05","cell-1")); Reject(basic,Place("card-05","home-1"));
        Reject(basic,Place("card-01","home-2")); Reject(basic,Place("missing","column-2"));
        Accept(basic,Place("card-07","column-2"));
        Check.That(basic.ExportAuthorityState().Entities["column-2"].Contents.SequenceEqual(new[]{"card-34","card-07","card-19","card-05"}));
        Accept(basic,new(){Kind="undo"}); Accept(basic,Place("card-01","home-1")); Accept(basic,new(){Kind="undo"});
        Check.That(basic.ExportAuthorityState().Entities["match"].Properties["home"]==0);
        Reject(Engine(Board(("column-1",["card-07","card-06"]),("column-2",["card-34"]))),Place("card-07","column-2"));
        Reject(Engine(Board(("column-1",["card-07"]),("column-2",["card-08"]))),Place("card-07","column-2"));
        var limited=Board(("column-1",["card-07","card-19","card-05"]),("column-2",["card-34"]),
            ("column-3",["card-13"]),("column-4",["card-26"]),("column-5",["card-39"]),("column-6",["card-52"]),
            ("cell-1",["card-02"]),("cell-2",["card-03"]),("cell-3",["card-04"]),("cell-4",["card-06"]));
        var capacity=Engine(limited);
        Reject(capacity,Place("card-07","column-7")); // Destination excluded: capacity 2, not 4.
        Accept(capacity,Place("card-07","column-2")); // Two other empty columns: capacity 4.
        limited.Entities["deck"].Contents.Remove("card-12"); limited.Entities["card-12"].Container="column-8"; limited.Entities["column-8"].Contents.Add("card-12");
        Reject(Engine(limited),Place("card-07","column-2")); // One empty column: capacity 2.
        foreach(var id in new[]{"card-04","card-06"}) { var c=limited.Entities[id];limited.Entities[c.Container!].Contents.Remove(id); c.Container="deck";limited.Entities["deck"].Contents.Add(id); }
        Accept(Engine(limited),Place("card-07","column-7")); // Two free cells: capacity 3.
        // A complete legal victory fixture; not a claim that a random deal was solved.
        var winning=Board(Enumerable.Range(1,4).Select(s=>($"column-{s}",Enumerable.Range(1,13).Reverse().Select(r=>$"card-{(s-1)*13+r:00}").ToArray())).ToArray());
        var winner=Engine(winning);
        for(var rank=1;rank<=13;rank++) for(var suit=1;suit<=4;suit++) Accept(winner,Place($"card-{(suit-1)*13+rank:00}",$"home-{suit}"));
        Check.That(winner.Project("p1").Ended && winner.Project("p1").Winners.SequenceEqual(new[]{"p1"}));
        Check.That(winner.ExportAuthorityState().Entities["match"].Properties["home"]==52); Reject(winner,new(){Kind="undo"});
        var package=Path.Combine(output,Guid.NewGuid()+".bgm"); ModulePackage.Pack(Source,package);
        var packed=ModulePackage.Load(package); Check.That(packed.Fingerprint==module.Fingerprint && packed.ResourceData!.Count==53);
        Check.That(packed.ResourceData!["player.presentation"].SequenceEqual(module.ResourceData!["player.presentation"]),"Player presentation lost during packaging");
        using(var human=new PlaySession(packed,42,maxSteps:1))
        {
            Check.That(human.SubmitHuman("p1",new(){Kind="start"}).Accepted);
            Check.That(human.SubmitHuman("p1",new(){Kind="deal"}).Accepted && human.Fault==null,"Human play was stopped by CPU budget");
        }
        using(var cpu=new PlaySession(module,42,new Dictionary<string,string>{{"p1","cpu"}},maxSteps:1))
        { Check.That(cpu.StepCpu()); Check.That(!cpu.StepCpu() && cpu.Fault?.Code=="step-limit"); }
        File.WriteAllText(Path.Combine(output,"fixture-victory.json"),Wire.Json(winner.ExportLog()));
        File.WriteAllText(Path.Combine(output,"fixture-initial.json"),Wire.Json(winning));
        var solution=Wire.Read<List<Command>>(File.ReadAllText(Source+"/solution-seed-1.json"));
        var solved=Engine(seed:1);
        foreach(var command in solution)
        {
            Check.That(solved.Actions("p1").Any(a=>Wire.Json(a.Command)==Wire.Json(command)),"Solution command was not offered");
            Accept(solved,command);
        }
        Check.That(solved.Project("p1").Ended && solved.Project("p1").Winners.SequenceEqual(new[]{"p1"}),"Seed 1 not solved");
        Check.That(Wire.Json(GameEngine.Replay(module,solved.ExportLog()).ExportAuthorityState())==Wire.Json(solved.ExportAuthorityState()));
        File.WriteAllText(Path.Combine(output,"seed-1-victory.json"),Wire.Json(solved.ExportLog()));
        Console.WriteLine("FreeCell: 20 seeded deals/replays; boundaries, rejection, undo, ordered hit testing, package, 52-card fixture victory passed.");
        Console.WriteLine($"FreeCell seed 1: {solution.Count} commands from fresh shuffle to victory; all offered, accepted and replayed.");
    }

    public static void Modes()
    {
        var module=DirectoryModule.Load(Source); var controls=new Dictionary<string,string>{{"p1","human"}};
        using IDebugSession local=new InProcessDebugSession(module,1,controls,"p1");
        using IDebugSession remote=ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",module,1,controls,"p1",true).Result;
        using var player=PlayerSession.CreateAsync(module,1,"p1").Result;
        var first=local.SendAsync(new("frame")).Result;
        foreach(var command in Wire.Read<List<Command>>(File.ReadAllText(Source+"/solution-seed-1.json")))
        {
            var request=new DebugRequest("submit",first.Frame.Revision,command);
            first=local.SendAsync(request).Result; var second=remote.SendAsync(request).Result;
            Check.That(first.Result?.Accepted==true && Wire.Json(first.Frame)==Wire.Json(second.Frame),"Play modes differ");
            var offer=player.Frame.Actions.Single(a=>Wire.Json(a.Command)==Wire.Json(command));
            var intent=offer.Gesture=="drag"?new InteractionIntent("drag",command.Entity,offer.Target):new("action",Action:offer.Id);
            Check.That(player.SubmitAsync(player.Frame.Revision,intent).Result.Accepted);
            Check.That(Wire.Json(player.Frame.View)==Wire.Json(first.Frame.View),"Player interaction differs");
        }
        Check.That(first.Frame.View.Ended && first.Frame.View.Winners.SequenceEqual(new[]{"p1"}));
        var recording=remote.SendAsync(new("recording")).Result.Recording!;
        Check.That(Wire.Json(recording)==Wire.Json(local.SendAsync(new("recording")).Result.Recording));
        Check.That(Wire.Hash(Wire.Json(recording.Replay().ExportAuthorityState()))==first.Frame.StateHash);
        Directory.CreateDirectory("artifacts/freecell-validation");
        File.WriteAllText("artifacts/freecell-validation/modes-victory.json",Wire.Json(recording));
        Console.WriteLine("FreeCell seed 1 victory: in-process Editor session, external Player debug session, graphical interaction adapter and recording replay agree.");
    }
}
