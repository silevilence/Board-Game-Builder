using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class PokerUnoOnlineTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        foreach(var count in new[] {4,5,6})
        {
            var preset=count+" 人";var module=GameRuntime.Load("samples/poker-uno",preset);
            using var host=new OnlineTransportTests.Host("samples/poker-uno",true,preset:preset,humanSeats:new HashSet<string> {"p2","p4"});
            using var inspect=new OnlineClient(host.Url,host.Key);
            Check.Throws(()=>inspect.JoinAsync(new(OnlineIdentity.For(GameRuntime.Load("samples/poker-uno",count==4?"5 人":"4 人")),"p2")).GetAwaiter().GetResult(),"version-mismatch");
            var info=await inspect.DescribeAsync();
            Check.That(info.Seats.Count==count && info.Seats.Single(s=>s.Id=="p1").Controller=="cpu" && info.Seats.All(s=>!s.Occupied));
            using var p2=new OnlinePlayerSession(module,new(host.Url.ToString(),host.Key,"p2"),Path.Combine(host.Root,"p2.json"));
            using var p4=new OnlinePlayerSession(module,new(host.Url.ToString(),host.Key,"p4"),Path.Combine(host.Root,"p4.json"));
            using var audience=new OnlinePlayerSession(module,new(host.Url.ToString(),host.Key,Audience:true),Path.Combine(host.Root,"audience.json"));
            await p2.ConnectAsync();Check.That(!p2.Frame!.Started);
            await p4.ConnectAsync();await audience.ConnectAsync();
            await p2.RefreshAsync();Check.That(p2.Frame!.Controllers!["p1"]=="cpu" && p2.Frame.Controllers["p2"]=="human");
            var resumedHuman=false;var resumedCpu=false;
            for(var i=0;i<1200;i++)
            {
                await p2.RefreshAsync();await p4.RefreshAsync();await audience.RefreshAsync();
                var frame=p2.Frame!;Check.That(frame.Fault==null && !frame.Closed,Wire.Json(frame.Fault));
                Check.That(!audience.Frame!.View.Entities.Any(e=>e.Container?.StartsWith("hand-")==true),"Public spectator saw private cards");
                Check.That(audience.Frame.Actions.Count==0);
                foreach(var player in new[] {p2,p4})
                    Check.That(player.Frame!.View.Entities.All(e=>e.Container?.StartsWith("hand-")!=true || e.Owner==player.Frame.View.Seat));
                if(frame.View.Ended)break;
                var current=new[] {p2,p4}.FirstOrDefault(p=>p.Frame!.Actions.Count>0);
                if(current==null)
                {
                    if(!resumedCpu) {await p2.DisconnectAsync();await Task.Delay(120);await p2.ConnectAsync();resumedCpu=true;}
                    await Task.Delay(60);continue;
                }
                if(!resumedHuman) {await current.DisconnectAsync();await current.ConnectAsync();resumedHuman=true;}
                var active=current.Frame!;
                var result=await current.SubmitAsync(active.Revision,new("action",Action:active.Actions[0].Id));
                Check.That(result.Result.Accepted || result.Result.Reason=="stale-revision");
                var stale=await current.SubmitAsync(-1,new("action",Action:active.Actions[0].Id));
                Check.That(!stale.Result.Accepted && current.Connected,"A stale UI action disconnected the transport");
            }
            Check.That(p2.Frame!.View.Ended && resumedHuman && resumedCpu,"Online session did not finish or exercise both recovery cases");
            var record=await p2.RecordingAsync();Check.That(record.Replay().Frame!.View.Ended);
            Check.That((record with {Identity=record.Identity with {Protocol=1},Frames=record.Frames.Select(f=>f with {Controllers=null}).ToArray()}).Replay().Frame!.View.Ended,"Legacy protocol recording no longer replays");
            File.WriteAllText(Path.Combine(host.Root,"player-recording.json"),Wire.Json(record));
            await p2.DisconnectAsync();
            using(var restored=new OnlinePlayerSession(module,new(host.Url.ToString(),host.Key,"p2"),Path.Combine(host.Root,"p2.json")))
            {await restored.ConnectAsync();Check.That(restored.Frame!.View.Ended && restored.Frame.View.Seat=="p2");}
            File.WriteAllText(Path.Combine(host.Root,"uno-result.json"),Wire.Json(new {success=true,count,resumedHuman,resumedCpu,replay=true}));
            Console.WriteLine($"UNO online {count}: completed with p1 CPU, two humans, spectator and recovery");
        }
    }
}
