using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class LocalTableTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        var module=DirectoryModule.Load("samples/quick-cards");
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
    }
}
