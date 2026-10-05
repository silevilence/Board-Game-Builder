using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class SampleTests
{
    public static void Run()
    {
        var module=DirectoryModule.Load("samples/quick-cards");using var session=new PlaySession(module,42);
        var humanActions=0;
        while(!session.Ended && session.Fault==null && session.Steps<20)
        {
            if(session.IsCpu) session.StepCpu();
            else { var action=session.Actions()[0];Check.That(session.SubmitHuman(session.CurrentSeat,action.Command).Accepted);humanActions++; }
        }
        Check.That(session.Ended && session.Fault==null && humanActions==4,Wire.Json(session.Fault));
        var replay=GameEngine.Replay(module,session.ExportAuthorityLog());
        Check.That(Wire.Hash(Wire.Json(replay.ExportAuthorityState()))==session.AuthorityStateHash());
        Check.That(session.ExportAuthorityLog().Entries.SelectMany(e=>e.Effects).Any(e=>e.Command.Kind=="shuffle"));
    }
}
