using BoardGame.Editor;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;
using BoardGame.View;

namespace Engine.Tests;

public static class PokerUnoTests
{
    private static LoadedModule Load(int count=4)=>GameRuntime.Load("samples/poker-uno",count+" 人");
    private static Dictionary<string,string> Humans(LoadedModule module)=>module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human");
    private static void Act(PlaySession session,string kind)
    {
        var offer=session.Actions().First(a=>a.Command.Kind==kind);
        var result=session.SubmitHuman(session.CurrentSeat,offer.Command);
        Check.That(result.Accepted,kind+": "+result.Reason+Wire.Json(session.ExportDiagnostics().TakeLast(3)));
    }
    public static void Run()
    {
        foreach(var count in new[] {4,5,6})
        {
            var module=Load(count);Check.That(module.Initial.Seats.Count==count);
            using var session=new PlaySession(module,12,Humans(module));Act(session,"start");Act(session,"open");
            var own=session.View("p1");
            Check.That(own.Entities.Single(e=>e.Id=="deck").Count==51-7*count);
            Check.That(own.Entities.Count(e=>e.Container=="hand-p1")==7);
            Check.That(!own.Entities.Any(e=>e.Container=="hand-p2" || e.Container=="deck"));
            Check.That(module.Initial.Entities.Values.Count(e=>e.Properties.ContainsKey("rank"))==52);
            var before=session.AuthorityStateHash();
            Check.That(!session.SubmitHuman("p2",new() {Kind="draw"}).Accepted);
            var top=own.Entities.Single(e=>e.Container=="table");
            var illegal=own.Entities.FirstOrDefault(e=>e.Container=="hand-p1" && e.Properties["suit"]!=top.Properties["suit"] && e.Properties["rank"]!=top.Properties["rank"]);
            if(illegal!=null)Check.That(!session.SubmitHuman("p1",new() {Kind="play",Entity=illegal.Id}).Accepted);
            Check.That(before==session.AuthorityStateHash());
            var scene=TableScene.From(own,module.ResourceData);
            Check.That(scene.Resources.Keys.SequenceEqual(new[] {"cards"}),"Atlas bytes were split or duplicated by face");
            Check.That(scene.Items.Where(i=>i.Kind=="card").All(i=>ImageReference.Parse(i.Resource!).IsRegion));
            Check.That(scene.Items.Any(i=>i.Kind=="back" && i.Resource!=null));
            Check.That(Wire.Json(session.ExportRecording().Replay().Project("p1"))==Wire.Json(own));
        }
        Edges();ResourcesAndPresets();
    }
    private static LoadedModule Position(string[] hand,string top,string[]? deck=null,string[]? spent=null,string phase="play")
    {
        var module=Load();var state=module.Initial;state.Phase=phase;
        foreach(var container in state.Entities.Values.Where(e=>e.IsContainer))container.Contents.Clear();
        var reserved=hand.Append(top).Concat(deck ?? []).Concat(spent ?? []).ToHashSet();
        foreach(var card in state.Entities.Values.Where(e=>e.Properties.ContainsKey("rank")))
        {
            var where=hand.Contains(card.Id)?"hand-p1":card.Id==top?"table":deck?.Contains(card.Id)==true?"deck":spent?.Contains(card.Id)==true?"spent":"hand-p2";
            card.Container=where;state.Entities[where].Contents.Add(card.Id);
            card.Owner=where=="hand-p1"?"p1":where=="hand-p2"?"p2":null;
            card.Visibility=where is "table" or "spent"?"public":where=="deck"?"hidden":"owner";
            card.FaceUp=card.Visibility=="public";
        }
        return TestModules.Rebuild(module);
    }
    private static void Edges()
    {
        // Hearts and diamonds have the same colour, but different suits; face ranks match as ordinary ranks.
        using(var s=new PlaySession(Position(["card-15","card-27","card-25"],"card-38"),1,Humans(Load())))
        {
            var legal=s.Actions().Where(a=>a.Command.Kind=="play").Select(a=>a.Command.Entity).ToArray();
            Check.That(legal.Contains("card-27") && legal.Contains("card-25") && !legal.Contains("card-15"));
        }
        using(var s=new PlaySession(Position(["card-25"],"card-38"),1,Humans(Load())))
        {Act(s,"play");Check.That(s.Ended && s.View("p1").Winners.SequenceEqual(new[] {"p1"}));}
        using(var s=new PlaySession(Position(["card-15"],"card-38",["card-25"]),1,Humans(Load())))
        {
            Act(s,"draw");Check.That(s.View("p1").Phase=="drawn");
            Check.That(s.Actions().Any(a=>a.Command.Kind=="pass") && s.Actions().Single(a=>a.Command.Kind=="play").Command.Entity=="card-25");
            Act(s,"pass");Check.That(s.CurrentSeat=="p2");
        }
        using(var s=new PlaySession(Position(["card-15"],"card-38",["card-03"]),1,Humans(Load())))
        {Act(s,"draw");Check.That(s.Actions().Single().Command.Kind=="pass");Act(s,"pass");}
        using(var s=new PlaySession(Position(["card-15"],"card-38",spent:["card-25","card-26"]),1,Humans(Load())))
        {
            Act(s,"recycle");
            Check.That(s.View("p1").Entities.Single(e=>e.Id=="deck").Count==2);
            Check.That(s.View("p1").Entities.All(e=>e.Container!="deck"),"Recycled card identities leaked to recycler");
            Check.That(s.View("p1").Entities.Single(e=>e.Container=="table").Id=="card-38");
            Check.That(Wire.Json(s.ExportRecording().Replay().Project("p1"))==Wire.Json(s.View("p1")));
        }
        var blocked=Position(["card-15"],"card-38");
        foreach(var card in blocked.Initial.Entities.Values.Where(e=>e.Properties.ContainsKey("rank") && e.Id!="card-38"))
        {card.Properties["suit"]=2;card.Properties["rank"]=2;}
        blocked=TestModules.Rebuild(blocked);
        using(var s=new PlaySession(blocked,1,Humans(blocked)))
        {for(var i=0;i<4;i++)Act(s,"blocked");Check.That(s.Ended && s.View("p1").Winners.Count==0);}
    }
    private static void ResourcesAndPresets()
    {
        var module=Load();
        var refs=module.Initial.Entities.Values.Where(e=>e.Properties.ContainsKey("rank")).SelectMany(e=>e.FaceResources).Distinct().ToArray();
        Check.That(refs.Length==53);foreach(var r in refs)ImageReference.Parse(r).Validate(module.ResourceData!);
        Check.Throws(()=>ImageReference.Parse("cards#-1,0,1,1"),"Invalid image region");
        Check.Throws(()=>ImageReference.Parse("cards#0,0,0,1"),"Empty image region");
        Check.Throws(()=>ImageReference.Parse("cards#99999,0,1,1").Validate(module.ResourceData!),"outside resource");
        Check.Throws(()=>GameRuntime.Load("samples/poker-uno","3 人"),"Unknown definition preset");
        var folder=Path.GetFullPath("artifacts/poker-uno/package-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        var package=Path.Combine(folder,"game.bgm");GamePackage.Pack("samples/poker-uno",package);
        using(var zip=System.IO.Compression.ZipFile.OpenRead(package))Check.That(zip.Entries.Count(e=>e.FullName.EndsWith(".png"))==1);
        foreach(var count in new[] {4,5,6})
        {
            var packed=GameRuntime.Load(package,count+" 人");Check.That(packed.Fingerprint==Load(count).Fingerprint);
            GameRuntime.ValidateSnapshot(packed);
        }
        var document=ModuleDocument.Open("samples/poker-uno");
        document.EditStartup(root=>root["plugins"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="definition.module")!["configuration"]!["path"]="module-6.json");
        Check.That(document.DefinitionPath=="module-6.json" && document.Snapshot().Initial.Seats.Count==6);
        document.Undo();Check.That(document.Snapshot().Initial.Seats.Count==4);
        document.Redo();document.Save(Path.Combine(folder,"author"));
        Check.That(ModuleDocument.Open(Path.Combine(folder,"author")).Snapshot().Initial.Seats.Count==6);
    }
    public static void Batch()
    {
        foreach(var count in new[] {4,5,6})
        {
            var results=BatchRunner.Run(Load(count),1,20,$"artifacts/poker-uno/batch-{count}",500,r=>Console.WriteLine($"UNO {count} seats seed {r.Seed}: {r.Success}, {r.Steps} steps"));
            Check.That(results.All(r=>r.Success),Wire.Json(results.Where(r=>!r.Success)));
        }
    }
    public static void Modes()=>ModesAsync().GetAwaiter().GetResult();
    private static async Task ModesAsync()
    {
        foreach(var count in new[] {4,5,6})
        foreach(var mode in Enum.GetValues<LocalTableMode>())
        foreach(var mixed in new[] {false,true})
        {
            var module=Load(count);
            using var local=new LocalTableSession(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,s=>!mixed || s.Id is "p2" or "p3"?"human":"cpu"),mode);
            for(var i=0;i<500 && !local.Frame.Ended;i++)
            {
                Check.That(local.Frame.Fault==null,Wire.Json(local.Frame.Fault));
                if(local.Frame.Cpu) {Check.That(await local.StepCpuAsync());continue;}
                if(local.Frame.WaitingFor is {} waiting)
                {Check.That(local.Frame.Views.Count==0);Check.That(await local.TakeSeatAsync(local.Frame.Revision,waiting));continue;}
                foreach(var f in local.Frame.Views)
                    Check.That(f.View.Entities.All(e=>e.Container?.StartsWith("hand-")!=true || e.Owner==f.View.Seat),"Foreign hand in local view");
                var view=local.Frame.Views.First(v=>v.Actions.Count>0);
                var result=await local.SubmitAsync(local.Frame.Revision,view.View.Seat,new("action",Action:view.Actions[0].Id));Check.That(result.Accepted);
            }
            Check.That(local.Frame.Ended && local.Frame.Fault==null);
        }
        var m=Load(6);
        using var all=new LocalTableSession(m,1,m.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"),LocalTableMode.HotSeat);
        while(!all.Frame.Ended && all.Frame.Fault==null)
        {
            Check.That(all.Frame.Views.Single().View.Seat==SeatView.AudienceSeat);
            Check.That(!all.Frame.Views[0].View.Entities.Any(e=>e.Properties.ContainsKey("rank") && e.Container!.StartsWith("hand-")));
            Check.That(await all.StepCpuAsync());
        }
        Check.That(all.Frame.Ended && all.Frame.Fault==null);
    }
}
