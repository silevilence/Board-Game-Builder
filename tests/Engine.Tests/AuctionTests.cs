using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class AuctionTests
{
    public static void Run()
    {
        var module=TestModules.Load("samples/sealed-auction");
        foreach(var mixed in new[] {false,true})
        for(uint seed=1;seed<=20;seed++)
        {
            using var session=new PlaySession(module,seed,module.Initial.Seats.ToDictionary(s=>s.Id,s=>mixed && s.Id=="p1"?"human":"cpu"));
            for(var i=0;i<30 && !session.Ended && session.Fault==null;i++)
            {
                if(session.IsCpu) Check.That(session.StepCpu());
                else Check.That(session.SubmitHuman(session.CurrentSeat,session.Actions()[0].Command).Accepted);
                foreach(var s in module.Initial.Seats)
                    if(session.View(s.Id).Secret is {Status:"collecting" or "closed"} secret) Check.That(secret.Revealed==null);
            }
            Check.That(session.Ended && session.Fault==null,session.Fault?.Detail ?? "Auction not ended");
            Check.That(Wire.Hash(Wire.Json(session.ExportRecording().Replay().ExportAuthorityState()))==session.AuthorityStateHash());
        }
        foreach(var deadline in new[] {false,true})
        {
            using var session=new PlaySession(module,7,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"));
            Check.That(session.SubmitHuman("p1",new() {Kind="open"}).Accepted);
            Check.That(!session.SubmitHuman("p2",new() {Kind="bid",Value=99}).Accepted);
            if(deadline)
                for(var i=0;i<12;i++) Check.That(session.SubmitHuman("p1",new() {Kind="tick"}).Accepted);
            else
                foreach(var s in module.Initial.Seats) Check.That(session.SubmitHuman(s.Id,new() {Kind="bid",Value=4}).Accepted);
            foreach(var kind in new[] {"close","reveal","settle"}) Check.That(session.SubmitHuman("p1",new() {Kind=kind}).Accepted);
            Check.That(session.Ended && session.View("p1").Winners.SequenceEqual(deadline?Array.Empty<string>():new[] {"p1"}));
            Check.That(!session.SubmitHuman("p1",new() {Kind="settle"}).Accepted);
            Check.That(Wire.Json(session.ExportRecording().Replay().Project("p1"))==Wire.Json(session.View("p1")));
        }
    }
}
