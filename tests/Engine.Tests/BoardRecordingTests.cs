using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class BoardRecordingTests
{
    public static void Run()
    {
        foreach(var source in new[]{"samples/quick-cards","samples/flow-cards","samples/wasm-counter"})
        {
            var module=GameRuntime.Load(source);SessionRecording recording;
            Dictionary<string,string> expected;
            using(var session=new PlaySession(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu")))
            {
                while(session.StepCpu()) { }
                Check.That(session.Ended && session.Fault==null);
                recording=session.ExportRecording();
                expected=module.Initial.Seats.ToDictionary(s=>s.Id,s=>Wire.Json(session.View(s.Id)));
            }
            var plugin=BoardRecordingPlugin.Create(()=>module);
            Check.That(plugin.Requires.SequenceEqual(new[]{"authority"}) && plugin.ServicePermissions["recording"]==AuthorityPlugin.Access);
            var original=Wire.Json(recording);
            foreach(var seat in module.Initial.Seats)
            {
                var projection=BoardRecordingPlugin.Observe(recording,seat.Id);
                Check.That(projection is not IAuthoritySession && projection is not IGameSession && projection is not IBoardCommandSource);
                for(var read=0;read<3;read++)
                {
                    var view=projection.View();Check.That(Wire.Json(view)==expected[seat.Id]);
                    view.Entities.Clear();
                }
                Check.That(Wire.Json(projection.View())==expected[seat.Id]);
            }
            Check.That(Wire.Json(recording)==original,"Observation changed recording");
            Check.Throws(()=>BoardRecordingPlugin.Observe(recording,"unknown-seat"),"seat");
            Check.Throws(()=>BoardRecordingPlugin.Observe(recording with {Hash="bad"},module.Initial.Seats[0].Id),"checksum");
        }
        // A player's historical projections cannot be interpreted as an authority record.
        var playerJson="{\"format\":2,\"seat\":\"p1\",\"frames\":[]}";
        Check.Throws(()=>Wire.Read<SessionRecording>(playerJson).Replay(),"seat");
        Check.Throws(()=>Wire.Read<SessionRecording>("{}").Replay(),"authority recording");
    }
}
