using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class HexTests
{
    public static void Run()
    {
        var module=TestModules.Load("samples/hex-skirmish");
        Check.That(module.Initial.Seats.Count==3 && module.Initial.Seats[0].Team==module.Initial.Seats[1].Team);
        using var session=new PlaySession(module,42);
        while(!session.Ended && session.Fault==null)
        {
            if(session.IsCpu) session.StepCpu();
            else
            {
                var offers=session.Actions();
                // A human can choose any displayed offer; selecting the first needs no other human.
                Check.That(offers.Count>0); Check.That(session.SubmitHuman(session.CurrentSeat,offers[0].Command).Accepted);
            }
        }
        Check.That(session.Ended && session.Fault==null,session.Fault?.Detail ?? "hex did not finish");
        var log=session.ExportAuthorityLog(); Check.That(log.Entries.Any(e=>e.Effects.Any(c=>c.Command.Kind=="roll")));
        Check.That(Wire.Hash(Wire.Json(GameEngine.Replay(module,log).ExportAuthorityState()))==session.AuthorityStateHash());
    }
}
