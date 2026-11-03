using System.Text.Json.Nodes;
using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace Engine.Tests;

public static class TeachingSamplesTests
{
    private static void Act(PlaySession game,string id)
    {
        var action=game.Actions().Single(a=>a.Id==id);
        var result=game.SubmitHuman(game.CurrentSeat,action.Command);
        Check.That(result.Accepted && game.Fault==null,id+": "+result.Reason+" "+game.Fault?.Detail);
    }
    private static void Replay(PlaySession game)
    {
        var replay=game.ExportRecording().Replay();
        Check.That(game.AuthorityStateHash()==Wire.Hash(Wire.Json(replay.ExportAuthorityState())),"Replay state differs");
        Check.That(Wire.Json(game.View("p1"))==Wire.Json(replay.Project("p1")),"Replay projection differs");
    }
    private static LoadedModule Packed(string sample)
    {
        var directory=Path.GetFullPath(".scratch/teaching-samples");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,sample+"-"+Guid.NewGuid()+".bgm");
        GamePackage.Pack("samples/"+sample,path);
        var packed=GameRuntime.Load(path);
        Check.That(packed.Fingerprint==GameRuntime.Load("samples/"+sample).Fingerprint,"Package identity differs");
        return packed;
    }
    public static void Polyomino()
    {
        var module=GameRuntime.Load("samples/polyomino");var packed=Packed("polyomino");
        Check.That(module.Initial.Layout!.Kind=="square");
        var cells=module.Initial.Entities.Values.Where(e=>e.Properties.ContainsKey("piece")).ToArray();
        Check.That(cells.Length==16 && cells.All(e=>e.Owner=="p1" && e.Properties["viewWidth"]==58 && e.Properties["viewHeight"]==58));
        var doc=ModuleDocument.Open("samples/polyomino");
        doc.EditConfiguration(root=>
        {
            var prototype=Authoring.Find(root,"prototypes","cell");
            var props=prototype["components"]!.AsArray().Single(c=>c!["type"]!.GetValue<string>()=="properties")!["data"]!;
            props["viewWidth"]=50;props["material"]=3;
        });
        var inherited=doc.Snapshot();
        Check.That(inherited.Initial.Entities.Values.Where(e=>e.Properties.ContainsKey("piece")).All(e=>e.Properties["viewWidth"]==50));
        Check.That(inherited.Initial.Entities["I-0"].Properties["material"]==3 && inherited.Initial.Entities["B-0"].Properties["material"]==2);
        doc.EditConfiguration(root=>Authoring.Find(root,"entities","B-0")["components"]!.AsArray()
            .Single(c=>c!["type"]!.GetValue<string>()=="properties")!["data"]!.AsObject().Remove("material"));
        Check.That(doc.Snapshot().Initial.Entities["B-0"].Properties["material"]==3,"Removing override did not restore inheritance");
        doc.Undo();doc.Undo();Check.That(doc.Snapshot().Fingerprint==module.Fingerprint,"Prototype Undo changed source");
        using(var game=new PlaySession(module))
        {
            // Four rotations restore each cell, including non-corner drag handles.
            Act(game,"select-3");var initial=game.ExportAuthorityState();
            for(var i=0;i<4;i++)Act(game,"rotate");
            Check.That(cells.Where(e=>e.Properties["piece"]==3).All(e=>
                game.ExportAuthorityState().Entities[e.Id].X==initial.Entities[e.Id].X && game.ExportAuthorityState().Entities[e.Id].Y==initial.Entities[e.Id].Y));
            Act(game,"select-1");
            foreach(var bad in new[]{new Command{Kind="move",Entity="I-0",X=-1,Y=0},
                new(){Kind="move",Entity="I-0",X=1,Y=0},new(){Kind="move",Entity="I-1",X=0,Y=0},
                new(){Kind="move",Entity="I-0",X=0.2,Y=0},new(){Kind="select-piece",Value=9}})
            {
                var before=game.AuthorityStateHash();Check.That(!game.SubmitHuman("p1",bad).Accepted);
                Check.That(before==game.AuthorityStateHash(),"Illegal placement partially changed the puzzle");
            }
            Act(game,"rotate");
            var table=TableScene.From(game.View("p1"),module.Resources);var handle=table.Items.Single(i=>i.Entity=="I-0");
            var destination=handle.MoveBy(-5*64,0);
            var drag=InteractionResolver.Resolve(game.Actions(),new("drag","I-0",X:destination.X,Y:destination.Y));
            Check.That(game.SubmitHuman("p1",drag).Accepted,"Square drag was not snapped to the legal placement");
            Act(game,"select-2");
            var beforeOverlap=game.AuthorityStateHash();
            Check.That(!game.SubmitHuman("p1",new(){Kind="move",Entity="O-0",X=0,Y=0}).Accepted);
            Check.That(game.AuthorityStateHash()==beforeOverlap,"Overlap rejection changed state");
            Act(game,"place-1-0");Act(game,"return");
            Check.That(game.ExportAuthorityState().Entities["puzzle"].Properties["placed"]==1);
            Act(game,"place-1-0");Act(game,"select-3");Act(game,"place-2-0");
            Act(game,"select-4");Act(game,"rotate");Act(game,"place-1-2");
            Check.That(game.Ended && game.ExportAuthorityState().Winners.SequenceEqual(new[]{"p1"}));
            var final=game.ExportAuthorityState().Entities.Values.Where(e=>e.Properties.ContainsKey("piece")).ToArray();
            Check.That(final.Select(e=>(e.X,e.Y)).Distinct().Count()==16 && final.All(e=>e.X>=0 && e.X<4 && e.Y>=0 && e.Y<4));
            Replay(game);
        }
        for(uint seed=1;seed<=5;seed++)
        {
            string? hash=null;
            foreach(var input in new[]{module,packed})
            {
                using var game=new PlaySession(input,seed,new Dictionary<string,string>{{"p1","cpu"}},maxSteps:40);
                while(!game.Ended && game.Fault==null && game.Steps<40)Check.That(game.StepCpu());
                Check.That(game.Ended && game.Fault==null && game.ExportAuthorityState().Winners.SequenceEqual(new[]{"p1"}),game.Fault?.Detail ?? "CPU did not solve");
                Replay(game);if(hash!=null)Check.That(hash==game.AuthorityStateHash());hash=game.AuthorityStateHash();
            }
        }
        using var abandoned=new PlaySession(module);Act(abandoned,"give-up");
        Check.That(abandoned.Ended && abandoned.ExportAuthorityState().Winners.Count==0);Replay(abandoned);
    }
    public static void Supply()
    {
        var module=GameRuntime.Load("samples/flow-supply");var packed=Packed("flow-supply");
        Check.That(!module.RuntimeIdentity!.Startup.Plugins.Any(p=>p.Id.StartsWith("lua.") || p.Id=="wasm"));
        var graph=FlowFile.Parse(File.ReadAllText("samples/flow-supply/flow.json"));
        Check.That(!FlowFile.Scripts(graph).Any() && !FlowValidation.Validate(graph).Any(d=>d.Severity=="error"));
        var weatherSeeds=new Dictionary<int,uint>();
        for(uint seed=1;seed<=100 && weatherSeeds.Count<6;seed++)
        {
            using var game=new PlaySession(module,seed);Act(game,"depart");
            weatherSeeds.TryAdd(game.ExportAuthorityState().Entities["weather"].Properties["value"],seed);
        }
        Check.That(weatherSeeds.Count==6,"Seed set did not cover all weather faces");
        var endings=new HashSet<string>();
        foreach(var (weather,seed) in weatherSeeds)
        foreach(var kit in new[]{0,1,2})
        foreach(var route in new[]{"coast","ridge","home"})
        {
            string? expectedHash=null;
            foreach(var input in new[]{module,packed})
            {
                using var game=new PlaySession(input,seed);
                var stale=game.Actions().Single(a=>a.Id=="depart").Command;
                Act(game,kit==0?"empty":kit==1?"rope":"lamp");
                Check.That(game.ExportAuthorityState().Flow!.Node=="pack" && !game.Ended,"complete:false left the node");
                var before=game.AuthorityStateHash();Check.That(!game.SubmitHuman("p1",stale).Accepted);
                Check.That(game.AuthorityStateHash()==before,"Stale action changed state");
                Act(game,"depart");Check.That(game.ExportAuthorityState().Entities["weather"].Properties["value"]==weather);
                // Restore at a waiting boundary without rerolling weather.
                var recording=game.ExportRecording();using var restored=PlaySession.Restore(input,recording);
                Check.That(Wire.Json(game.Actions())==Wire.Json(restored.Actions()));
                Act(game,route);Act(restored,route);
                Check.That(game.AuthorityStateHash()==restored.AuthorityStateHash());
                var equipped=kit==(route=="coast"?2:1);var clear=weather>=4;
                var expected=route=="home"?"retreat":clear && equipped?"bright":clear || equipped?"delivered":"failed";
                var final=game.ExportAuthorityState();
                Check.That(game.Ended && game.Fault==null && final.Flow!.Node==expected,$"{weather}/{kit}/{route}: expected {expected}");
                Check.That(final.Winners.Count==(expected is "bright" or "delivered"?1:0));
                endings.Add(expected);Replay(game);
                if(expectedHash!=null)Check.That(expectedHash==game.AuthorityStateHash(),"Directory/package differs");
                expectedHash=game.AuthorityStateHash();
            }
        }
        Check.That(endings.SetEquals(new[]{"bright","delivered","failed","retreat"}));
        using var replace=new PlaySession(module);Act(replace,"rope");Act(replace,"lamp");Act(replace,"empty");
        Check.That(replace.ExportAuthorityState().Entities["kit"].Properties["choice"]==0 && replace.ExportAuthorityState().Flow!.Node=="pack");
    }
}
