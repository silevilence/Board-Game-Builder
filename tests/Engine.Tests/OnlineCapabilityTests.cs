using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;
namespace Engine.Tests;

public static class OnlineCapabilityTests
{
    public static void Run()
    {
        var module=GameRuntime.Load("samples/flow-cards");
        using var server=new OnlineSession(module,7,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human"));
        var leases=module.Initial.Seats.Select(s=>server.Join(server.Identity,s.Id)).ToDictionary(s=>s.Seat);
        var audience=server.Watch(server.Identity);var sequence=leases.Keys.ToDictionary(s=>s,_=>1L);
        for(var step=0;step<100 && !server.Status.Ended;step++)
        {
            var before=Wire.Json(server.ExportAuthorityRecording());
            foreach(var seat in leases.Keys.ToArray())
            {
                var lease=leases[seat];server.Disconnect(lease.Token);
                var resume=server.Resume(server.Identity,lease.ResumeToken,-1);leases[seat]=resume.Lease;
                var replica=new OnlineReplica(seat);foreach(var frame in resume.Frames) replica.Apply(frame);
                ISeatProjection projection=replica;
                Check.That(projection is not IAuthoritySession && projection is not IBoardCommandSource);
                for(var i=0;i<20;i++) projection.View().Entities.Clear();
                Check.That(replica.View().Entities.Count==server.Read(resume.Lease.Token).View.Entities.Count,"Projection copy leaked mutation");
            }
            Check.That(Wire.Json(server.ExportAuthorityRecording())==before,"Reconnect or repeated projection advanced flow");
            var active=leases.Values.Select(l=>(Lease:l,Frame:server.Read(l.Token))).First(p=>p.Frame.Actions.Count>0);
            var receipt=server.Submit(active.Lease.Token,new(sequence[active.Lease.Seat],active.Frame.Revision,active.Lease.Seat,active.Frame.Actions[0].Command));
            sequence[active.Lease.Seat]=receipt.NextSequence;Check.That(receipt.Result.Accepted);
            Check.That(server.Read(audience.Token).Actions.Count==0);
        }
        Check.That(server.Status.Ended,"Flow network game did not finish");
        var replay=server.ExportPlayerRecording(audience.Token).Replay();
        Check.That(replay.View().Ended && replay is ISeatProjection);
        var deps=File.ReadAllText("src/Server/bin/Release/net10.0/Server.deps.json");
        Check.That(!deps.Contains("Rendering.WindowsDX") && !deps.Contains("BoardGame.Presentation") && !deps.Contains("Avalonia"),"Headless server acquired display implementation");
        Check.That(!typeof(PlaySession).Assembly.GetReferencedAssemblies().Any(a=>a.Name=="Networking"),"Local runtime requires networking");
    }
}
