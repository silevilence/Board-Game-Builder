using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class LocalTableTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var module=TestModules.Load("samples/quick-cards");
        foreach(var mode in Enum.GetValues<LocalTableMode>())
        foreach(var mixed in new[] {false,true})
        {
            using var table=new LocalTableSession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,s=>mixed && s.Id!="p1"?"cpu":"human"),mode);
            for(var step=0;step<80 && !table.Frame.Ended;step++)
            {
                var frame=table.Frame;
                if(frame.Cpu) {Check.That(await table.StepCpuAsync());continue;}
                if(frame.WaitingFor is {} next)
                {
                    Check.That(frame.Views.Count==0,"Hotseat retains old projection");
                    Check.That(!await table.TakeSeatAsync(frame.Revision,"unknown"));
                    Check.That(await table.TakeSeatAsync(frame.Revision,next));frame=table.Frame;
                }
                foreach(var view in frame.Views)
                {
                    Check.That(view.View.Entities.All(e=>e.Owner==null || e.Owner==view.View.Seat || e.Face==null || e.FaceUp),"Private face leaked");
                    if(view.View.Seat!=view.View.CurrentSeat) Check.That(view.Actions.Count==0);
                }
                var current=frame.Views.Single(v=>v.View.Seat==v.View.CurrentSeat);
                Check.That(current.Actions.Count>0);
                var intent=new InteractionIntent("action",Action:current.Actions[0].Id);
                Check.That(!(await table.SubmitAsync(frame.Revision,"unknown",intent)).Accepted);
                Check.That((await table.SubmitAsync(frame.Revision,current.View.Seat,intent)).Accepted);
                Check.That(!(await table.SubmitAsync(frame.Revision,current.View.Seat,intent)).Accepted,"Stale input accepted");
            }
            Check.That(table.Frame.Ended,"Mixed/local game did not finish");
        }
        var auction=TestModules.Load("samples/sealed-auction");var humans=auction.Initial.Seats.ToDictionary(s=>s.Id,_=>"human");
        using(var split=new LocalTableSession(auction,42,humans,LocalTableMode.SplitScreen))
        {
            Check.That((await split.SubmitAsync(split.Frame.Revision,"p1",new("action",Action:"open"))).Accepted);
            Check.That(split.Frame.Views.All(v=>v.Actions.Any(a=>a.Id=="bid3")));
            Check.That((await split.SubmitAsync(split.Frame.Revision,"p2",new("action",Action:"bid3"))).Accepted,"Simultaneous non-current UI submission rejected");
            Check.That(split.Frame.Views.Single(v=>v.View.Seat=="p1").View.Secret!.Revealed==null);
        }
        using(var hotseat=new LocalTableSession(auction,42,humans,LocalTableMode.HotSeat))
        {
            await hotseat.TakeSeatAsync(hotseat.Frame.Revision,"p1");
            Check.That((await hotseat.SubmitAsync(hotseat.Frame.Revision,"p1",new("action",Action:"open"))).Accepted);
            Check.That((await hotseat.SubmitAsync(hotseat.Frame.Revision,"p1",new("action",Action:"bid3"))).Accepted);
            Check.That(await hotseat.HandoffAsync(hotseat.Frame.Revision,"p1"));Check.That(hotseat.Frame.Views.Count==0);
            for(var i=0;i<25 && !hotseat.Frame.Ended;i++)
            {
                var frame=hotseat.Frame;
                if(frame.WaitingFor is {} next) {Check.That(next=="p1");await hotseat.TakeSeatAsync(frame.Revision,next);continue;}
                var view=frame.Views.Single();var offer=view.Actions.Single();
                Check.That((await hotseat.SubmitAsync(frame.Revision,"p1",new("action",Action:offer.Id))).Accepted);
            }
            Check.That(hotseat.Frame.Ended,"Locked coordinator could not resolve absent seats");
        }
    }
}
