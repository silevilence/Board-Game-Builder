using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Lua;
using BoardGame.Runtime;

// Rule fixtures deliberately use the real sample script and kernel transactions, without view assets.
var root=Path.GetFullPath("samples/cursed-investigators");
if(args.FirstOrDefault()=="play")
{
    var seed=args.Length>1?uint.Parse(args[1]):1;
    var module=GameRuntime.Load(root);
    using var game=new PlaySession(module,seed,maxSteps:2400);
    foreach(var setting in args.Skip(2).Where(a=>a.StartsWith("profession",StringComparison.Ordinal)))
        game.SubmitHuman("p1",game.Actions().Single(a=>a.Command.Kind=="profession"&&a.Command.Value==int.Parse(setting[^1..])).Command);
    foreach(var setting in args.Skip(2).Where(a=>a is "beginner" or "campaign"))
        game.SubmitHuman("p1",game.Actions().Single(a=>a.Command.Kind==setting&&a.Command.Value==1).Command);
    game.SetController("p1","cpu");
    while(!game.Ended&&game.Fault==null)
    {
        if(!game.StepCpu())break;
        if(game.Steps%25==0)
        {
            Console.WriteLine($"Step {game.Steps}: "+Wire.Json(game.ExportAuthorityState().Entities["match"].Properties));
            File.WriteAllText($".scratch/cursed-investigators/development/frame-{seed}.json",Wire.Json(game.View("p1")));
        }
    }
    var state=game.ExportAuthorityState();
    var result=new{seed,game.Ended,game.Steps,game.Fault,state.Winners,properties=state.Entities["match"].Properties,hash=game.AuthorityStateHash()};
    Directory.CreateDirectory(".scratch/cursed-investigators/development");
    var suffix=seed+"-"+string.Join('-',args.Skip(2));
    File.WriteAllText($".scratch/cursed-investigators/development/play-{suffix}.json",Wire.Json(result));
    File.WriteAllText($".scratch/cursed-investigators/development/actions-{suffix}.json",Wire.Json(game.ExportAuthorityLog().Entries.Where(e=>e.Submission.Accepted&&e.Submission.Execution=="player").Select(e=>e.Submission.Command).ToArray()));
    Console.WriteLine(Wire.Json(result));
    if(game.Fault!=null)throw new Exception(Wire.Json(game.Fault));
    return;
}
var source=File.ReadAllText(Path.Combine(root,"game.lua"));
using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"position.json")));
var initial=Wire.Read<GameState>(document.RootElement.GetProperty("state").GetRawText());
var passed=new List<string>();
void Check(bool value,string message) {if(!value)throw new Exception(message);}
GameState Fixture(int objectId=1)
{
    var s=Wire.Copy(initial);s.Flow=new(){DefinitionHash="rule-fixture",Node="test"};
    var p=s.Entities["match"].Properties;
    foreach(var (k,v) in new Dictionary<string,int>{{"stage",6},{"health",9},{"maxHealth",9},{"profession",2},{"knowledge",6},{"initialKnowledge",2},{"luck",4},{"resolve",4},{"object",objectId},{"current",1},{"game",1}})p[k]=v;
    for(var i=1;i<=6;i++){var d=s.Entities["die"+i];d.Properties["status"]=1;d.Properties["value"]=i;}
    return s;
}
SortedDictionary<string,int> M(GameState s)=>s.Entities["match"].Properties;
List<ActionOffer> Offers(GameState s,string node="turn")=>Wire.Read<List<ActionOffer>>(LuaEvaluator.Invoke(source,"game.lua","actions",Wire.Json(new{view=new GameEngine(s).Project("p1"),parameters=new{node}})));
(GameState State,FlowOutcome Outcome) Apply(GameState s,string entry,Command? command=null)
{
    var engine=new GameEngine(s);
    var input=command==null?Wire.Json(new{state=s}):Wire.Json(new{view=engine.Project("p1"),command});
    var outcome=Wire.Read<FlowOutcome>(LuaEvaluator.Invoke(source,"game.lua",entry,input));
    Check(outcome.Commands.Count<=64,$"{entry}: effect budget {outcome.Commands.Count}");
    var accepted=engine.SubmitFlow(null,new(){Kind="fixture"},s.Flow!.Revision,tx=>
    {foreach(var c in outcome.Commands)tx.Apply(c);tx.Complete(s.Flow with {Revision=s.Flow.Revision+1});});
    Check(accepted.Accepted,entry+": "+accepted.Reason);
    return (engine.ExportAuthorityState(),outcome);
}
GameState Choose(GameState s,string kind,string? property=null,int? value=null,string? entity=null,string? node=null)
{
    var offer=Offers(s,node??"turn").FirstOrDefault(a=>a.Command.Kind==kind && (property==null||a.Command.Property==property) && (value==null||a.Command.Value==value) && (entity==null||a.Command.Entity==entity));
    Check(offer!=null,$"Missing legal {kind}/{property}/{value}/{entity}");return Apply(s,"choose",offer!.Command).State;
}
void Test(string name,Action body) {body();passed.Add(name);Console.WriteLine("PASS "+name);}
void Dice(GameState s,params int[] values)
{for(var i=1;i<=6;i++){var p=s.Entities["die"+i].Properties;p["status"]=i<=values.Length?1:0;p["value"]=i<=values.Length?values[i-1]:1;p["selected"]=0;}}
Test("all-object-and-spirit-challenges",()=>
{
    int[][][] objects=[[[2,2,2],[2,2,2,2],[1,2,3,4,5]],[[4,4,4,4],[5,5,5,5],[6,6,6,6,6]],[[1,1,1,1],[3,3,3,3],[5,5,5,5,5]],[[1,1,1],[1,2,3,4,5],[2,3,4,5,6]],[[1,2,3,4],[2,2,2,2],[1,2,3,4,5]],[[1,2,3,4],[2,2,3,3],[1,2,3,4,5]],[[1,2,3,4],[2,2,2,2],[2,2,2,2,2]],[[1,2,3,4],[4,5,6,4],[1,2,3,4,5]],[[4,5,6],[2,2,2,2],[2,2,2,2,2]],[[2,2,2],[4,4,4,4],[6,6,6,6,6]],[[2,2,3,3],[2,2,2,2],[2,2,2,2,2]],[[4,1,1,1],[1,2,2,2],[2,2,1,1,1]],[[6,6,6,6],[2,2,2,2,2],[1,2,3,4,5]],[[3,3,4,5],[2,2,3,3],[1,2,3,4,5,6]]];
    int[][][] spirits=[[[2,2,3,3,3]],[[4,4,5,6,4]],[[1,2,3,4,5]],[[2,2,2,2]],[[1,2,3,4,5],[1,2,3,4,5,6]],[[2,2,2,3,3,3],[1,2,3,4,5]],[[1,2,3,4,5],[3,3,3,3,3]],[[1,2,3,4],[5,5,6,6,5,5]],[[2,2,2,2,2],[3,3,3,3,3]],[[1,2,3,4,5],[5,5,6,6,5,5]],[[1,2,3,4,5,6],[6,6,6,6,6,6]],[[1,2,3,4,5,6],[1,2,3,4,5,6]]];
    for(var category=0;category<2;category++)for(var i=0;i<(category==0?objects:spirits).Length;i++)
    {
        var cases=(category==0?objects:spirits)[i];
        for(var challenge=0;challenge<cases.Length;challenge++)
        {
            var s=Fixture(category==0?i+1:1);M(s)["machine"]=2;
            if(category==1){M(s)["objectDone"]=7;M(s)["spirit"]=i+1;}
            Dice(s,cases[challenge]);s=Choose(s,"challenge",value:challenge+1);
            Check(M(s)["challenged"]==1 && M(s)[category==0?"objectDone":"spiritDone"]==1<<challenge,"challenge bookkeeping");
            Check(!Offers(s).Any(a=>a.Command.Kind=="challenge"),"Second challenge offered in same turn");
        }
    }
    var bad=Fixture(6);Dice(bad,2,2,2,2);Check(!Offers(bad).Any(a=>a.Command.Kind=="challenge"&&a.Command.Value==2),"Pairs must be distinct");
});
Test("tracks-professions-and-beginner",()=>
{
    for(var profession=1;profession<=3;profession++)for(var character=1;character<=3;character++)
    {
        var s=Fixture();M(s)["stage"]=1;M(s)["profession"]=profession;M(s)["character"]=character;
        s=Choose(s,"start",node:"configure");Check(M(s)["health"]==(character==1?5:4)+(profession==1?2:profession==2?4:3),"health track");
    }
    var repeated=Fixture();M(repeated)["stage"]=7;M(repeated)["knowledge"]=1;
    repeated=Choose(repeated,"attribute","knowledge",node:"reward");
    Check(M(repeated)["knowledge"]==2 && repeated.Entities["status"].Properties["knowledge"]==4,"Repeated knowledge slot must advance without numeric increase");
    Check(!Offers(repeated,"reward").Any(a=>a.Command.Property=="health"),"Standard health reward");
    M(repeated)["beginner"]=1;M(repeated)["health"]=4;repeated=Choose(repeated,"attribute","health",node:"reward");Check(M(repeated)["health"]==5,"Beginner heal");
});
Test("reserve-capacity-and-reward-dice",()=>
{
    var s=Fixture();s=Choose(s,"select",entity:"die1");s=Choose(s,"store","knowledge");
    Check(s.Entities["die1"].Properties["status"]==2,"Reserve immediate");
    s=Choose(s,"select",entity:"die2");Check(!Offers(s).Any(a=>a.Command.Kind=="store"&&a.Command.Target=="1"),"Occupied ability slot");
    s.Entities["log1"].Properties["available"]=1;s=Choose(s,"reward",value:1);
    Check(s.Entities["log1"].Properties["used"]==1 && s.Entities["reward1"].Properties["value"]==2,"Reward die");
    s=Choose(s,"clear");s=Choose(s,"select",entity:"reward1");
    Check(!Offers(s).Any(a=>a.Command.Kind is "adjust" or "store" or "reroll"),"Reward die must be immutable and challenge only");
});
Test("initial-modifiers-and-limits",()=>
{
    foreach(var (ability,values,expected) in new[]{(5,new[]{1,6,6},new[]{1,5,6}),(13,new[]{1,2,6},new[]{1,1,5})})
    {
        var s=Fixture();Dice(s,values);M(s)["cap1"]=ability;M(s)["modifier"]=1;
        for(var i=1;i<=values.Length;i++)s.Entities["die"+i].Properties["status"]=4;
        s=Apply(s,"modify").State;Check(Enumerable.Range(1,values.Length).Select(i=>s.Entities["die"+i].Properties["value"]).SequenceEqual(expected),"Initial modifiers");
    }
    var mirror=Fixture(3);Dice(mirror,1,3,5,5,6);M(mirror)["modifier"]=4;
    for(var i=1;i<=5;i++)mirror.Entities["die"+i].Properties["status"]=4;
    mirror=Apply(mirror,"modify").State;Check(mirror.Entities["die3"].Properties["value"]==2 && mirror.Entities["die4"].Properties["value"]==2 && mirror.Entities["die2"].Properties["value"]==3,"Mirror highest odd all");
    var zero=Fixture();M(zero)["knowledge"]=0;for(var i=1;i<=4;i++){zero.Entities["die"+i].Properties["status"]=2;zero.Entities["die"+i].Properties["slot"]=i;}
    zero=Apply(zero,"roll").State;Check(zero.Entities.Values.Count(e=>e.Id.StartsWith("die")&&e.Properties["status"]==3)==4 && !zero.Entities.Values.Any(e=>e.Id.StartsWith("die")&&e.Properties["status"]==4),"Reserve above knowledge");
});
Test("costs-and-object-restrictions",()=>
{
    var s=Fixture(5);M(s)["resolve"]=0;Check(!Offers(s).Any(a=>a.Command.Kind=="challenge"&&a.Command.Value is 1 or 3),"Music box resolve restriction");
    s=Fixture(11);M(s)["luck"]=1;Dice(s,2,2,3,3);Check(!Offers(s).Any(a=>a.Command.Kind=="challenge"),"Toy house extra die");
    Dice(s,2,2,3,3,6);s=Choose(s,"challenge",value:1);Check(s.Entities.Values.Count(e=>e.Id.StartsWith("die")&&e.Properties["status"]==1)==0,"Toy house pays extra");
    s=Fixture(7);Dice(s,1);s=Choose(s,"select",entity:"die1");Check(!Offers(s).Any(a=>a.Command.Kind=="store"&&a.Command.Property=="knowledge"),"Portrait needs spare die");
    s=Fixture(14);Check(Offers(s).All(a=>a.Command.Kind is "select" or "clear" or "machine"),"Machine selection precedes adjustments");
    s=Fixture();M(s)["cap2"]=7;M(s)["resolve"]=4;s=Choose(s,"select",entity:"die1");s=Choose(s,"adjust",value:1);Check(M(s)["resolve"]==2,"First determination double cost");s=Choose(s,"adjust",value:1);Check(M(s)["resolve"]==1,"Subsequent determination normal");
    s=Fixture();M(s)["cap2"]=16;s=Choose(s,"select",entity:"die1");s=Choose(s,"reroll",value:1);Check(M(s)["luck"]==3&&M(s)["resolve"]==3,"Roar first luck costs resolve");
    s=Fixture(4);M(s)["resolve"]=0;M(s)["luck"]=0;s=Choose(s,"select",entity:"die1");s=Choose(s,"reroll",value:1);Check(M(s)["health"]==7,"Voodoo chain once");
    s=Fixture(13);M(s)["health"]=1;s=Choose(s,"select",entity:"die1");var death=Apply(s,"choose",Offers(s).Single(a=>a.Command.Kind=="reroll"&&a.Command.Value==2).Command);Check(death.Outcome.Result=="dead"&&!death.Outcome.Commands.Any(c=>c.Kind=="roll"),"Death prevents reroll");
});
Test("end-effects-order-and-death",()=>
{
    foreach(var ability in new[]{2,8,9,10,11,12,14,18,22})
    {
        var s=Fixture();M(s)["endIndex"]=1;M(s)["cap1"]=ability;var after=Apply(s,"endTurn");
        if(ability==14)Check(after.Outcome.Result=="choice","Wraith choice");
        else Check(M(after.State)[ability==11?"resolve":ability==12?"luck":ability==22?"knowledge":"health"]<(ability is 11 or 12?4:ability==22?6:9),"End ability "+ability);
    }
    foreach(var id in new[]{2,8,10,12,14})
    {
        var s=Fixture(id);M(s)["endIndex"]=4;M(s)["healthLost"]=1;M(s)["machine"]=3;
        var after=Apply(s,"endTurn").State;Check(M(after)[id is 2 or 14?"luck":"health"]<(id is 2 or 14?4:9),"Object end "+id);
    }
    foreach(var id in new[]{1,6})
    {
        var s=Fixture(id);M(s)["resolve"]=0;s.Entities["die1"].Properties["status"]=2;s.Entities["die1"].Properties["slot"]=1;
        s=Apply(s,"endTurn").State;Check(s.Entities["die1"].Properties["status"]==1,"Forbidden reserve returned before end checks");
    }
    var dead=Fixture(8);M(dead)["health"]=1;var outcome=Apply(dead,"endTurn");Check(outcome.Outcome.Result=="dead"&&M(outcome.State)["health"]==0,"No post-death recovery");
});
Test("reveal-capture-journal-and-campaign-reset",()=>
{
    GameState Pair(int objectId,int spirit)
    {
        var s=Fixture(objectId);
        foreach(var id in new[]{"object"+objectId,"spirit"+spirit})
        {
            var e=s.Entities[id];s.Entities[e.Container!].Contents.Remove(id);e.Container="pair1";s.Entities["pair1"].Contents.Add(id);
        }
        s.Entities["slot1"].Properties["object"]=objectId;return s;
    }
    foreach(var (spirit,property,amount) in new[]{(1,"resolve",1),(3,"knowledge",1),(4,"luck",1),(9,"luck",2),(11,"resolve",2),(12,"knowledge",2)})
    {
        var s=Pair(1,spirit);var before=M(s)[property];s=Apply(s,"enter").State;
        Check(M(s)[property]==before-amount && M(s)["spirit"]==0,"Initial reveal must apply once without full identity");
        var projection=new GameEngine(s).Project("p1");Check(projection.Entities.All(e=>e.Id!="spirit"+spirit),"Hidden authority spirit leaked");
        s=Apply(s,"roll").State;Check(M(s)[property]==before-amount,"Reveal repeated on initial roll");
    }
    var capture=Pair(1,1);M(capture)["objectDone"]=7;M(capture)["stage"]=7;M(capture)["cap1"]=1;M(capture)["cap2"]=8;M(capture)["cap3"]=11;
    capture=Apply(capture,"afterReward").State;
    Check(capture.Entities["log1"].Properties["available"]==1 && capture.Entities["log1"].Properties["captured"]==0 && M(capture)["score"]==0,"Reward availability and capture journal have distinct timing");
    M(capture)["spiritDone"]=1;capture=Apply(capture,"afterReward").State;
    Check(capture.Entities["log1"].Properties["captured"]==1 && capture.Entities["observation1"].Properties["count"]==1 && M(capture)["score"]==4 && M(capture)["knowledge"]==2,"Encounter settlement");
    var saved=Wire.Copy(capture);M(saved)["stage"]=9;M(saved)["health"]=1;M(saved)["campaign"]=1;
    saved=Choose(saved,"nextGame",node:"campaign");
    Check(M(saved)["game"]==2 && M(saved)["health"]==8 && M(saved)["score"]==4 && saved.Entities["log1"].Properties["available"]==1,"Campaign reset retains score and rewards, restores role health");
    var end=Fixture();for(var i=1;i<=7;i++)end.Entities["slot"+i].Properties["done"]=1;
    M(end)["game"]=2;M(end)["campaign"]=1;M(end)["captured"]=13;M(end)["encounterWon"]=1;M(end)["endIndex"]=5;
    Check(Apply(end,"endTurn").Outcome.Result=="win","Campaign completion branch");
    var warp=Pair(1,12);M(warp)["pendingWarp"]=1;M(warp)["objectDone"]=7;M(warp)["cap1"]=26;M(warp)["cap2"]=13;M(warp)["cap3"]=27;var k=M(warp)["knowledge"];
    warp=Apply(warp,"afterReward").State;Check(M(warp)["knowledge"]==k-1,"Existing warp must apply without repeating reveal costs");
});
Test("remaining-initial-effects-and-ignore",()=>
{
    foreach(var ability in new[]{6,17,20})
    {
        var s=Fixture();Dice(s,3,3,4);M(s)["modifier"]=1;M(s)["cap1"]=ability;
        for(var i=1;i<=3;i++)s.Entities["die"+i].Properties["status"]=4;
        s.Entities["die4"].Properties["status"]=3;s.Entities["die4"].Properties["slot"]=1;
        var outcome=Apply(s,"modify");
        if(ability==20)Check(outcome.Outcome.Result=="choice","Optional blur mitigation");
        else Check(outcome.Outcome.Commands.Count(c=>c.Kind=="roll")== (ability==6?1:2),"Initial reroll set "+ability);
    }
    var box=Fixture(9);Dice(box,6,3);M(box)["modifier"]=4;
    box.Entities["die1"].Properties["status"]=4;box.Entities["die2"].Properties["status"]=3;
    Check(Apply(box,"modify").Outcome.Commands.Count(c=>c.Kind=="roll")==1,"Jack box rerolls reserves");
    var shield=Fixture();M(shield)["cap1"]=8;M(shield)["endIndex"]=1;shield.Entities["log6"].Properties["available"]=1;
    shield=Choose(shield,"reward","1",6);var hp=M(shield)["health"];shield=Apply(shield,"endTurn").State;Check(M(shield)["health"]==hp,"Ignored ability still fired");
    var shade=Fixture();M(shade)["cap3"]=23;Dice(shade,2,2,2);Check(!Offers(shade).Any(a=>a.Command.Kind=="challenge"),"Deception requires extra matching die");
    Dice(shade,2,2,2,2);shade=Choose(shade,"challenge",value:1);Check(shade.Entities.Values.Count(e=>e.Id.StartsWith("die")&&e.Properties["status"]==1)==0,"Deception payment");
});
Test("optional-costs-and-capture-rewards",()=>
{
    var shade=Fixture();M(shade)["stage"]=3;shade=Choose(shade,"choice","luck",node:"revealChoice");Check(M(shade)["luck"]==2,"Shade choice luck");
    shade=Fixture();M(shade)["stage"]=3;shade=Choose(shade,"choice","health",node:"revealChoice");Check(M(shade)["health"]==8,"Shade choice health");
    foreach(var cap in new[]{15,25})
    {
        var s=Fixture();M(s)["cap3"]=cap;Dice(s,1,1,1);s=Choose(s,"challenge",value:1);
        Check(M(s)["resolve"]==3,"Challenge extra cost "+cap);
    }
    var portrait=Fixture(7);Dice(portrait,2,3,4);portrait=Choose(portrait,"select",entity:"die1");
    portrait=Choose(portrait,"store","knowledge",3,"die1");
    Check(portrait.Entities["die3"].Properties["status"]==0 && portrait.Entities["die2"].Properties["status"]==1,"Portrait must discard the chosen die");
    foreach(var (id,property,amount) in new[]{(2,"luck",2),(7,"health",1),(8,"knowledge",1),(12,"resolve",2),(14,"health",1)})
    {
        var s=Fixture();M(s)[property]=2;s.Entities["log"+id].Properties["available"]=1;s=Choose(s,"reward",value:id);
        Check(M(s)[property]==2+amount && s.Entities["log"+id].Properties["used"]==1,"Capture reward "+id);
        Check(!Offers(s).Any(a=>a.Command.Kind=="reward" && a.Command.Value==id),"Reward used twice");
    }
    var mirror=Fixture();mirror.Entities["log3"].Properties["available"]=1;mirror=Choose(mirror,"reward",value:3,entity:"die1");
    Check(mirror.Entities["die1"].Properties["value"]==6,"Mirror reward");
    var ignored=Fixture(5);M(ignored)["resolve"]=0;ignored.Entities["log9"].Properties["available"]=1;ignored=Choose(ignored,"reward",value:9);
    Check(Offers(ignored).Any(a=>a.Command.Kind=="challenge"&&a.Command.Value==1),"Object-ignore reward");
});
Console.WriteLine(Wire.Json(new{success=true,checks=passed}));

