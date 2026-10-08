using BoardGame.Kernel;
using BoardGame.Networking;

namespace Engine.Tests;

public static class OnlineTests
{
    public static void Identity()
    {
        var module = TestModules.Load("samples/quick-cards");
        using var room = new OnlineSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, _ => "human"));
        Check.Throws(() => room.Join(new(2, module.Fingerprint)), "version-mismatch");
        Check.Throws(() => room.Join(new(1, "changed-script-or-plugin")), "version-mismatch");
        var first = room.Join(OnlineIdentity.For(module));
        Check.That(!room.Read(first.Token).Started);
        var second = room.Join(OnlineIdentity.For(module));
        Check.That(first.Seat != second.Seat && first.Token != second.Token);
        Check.That(room.Read(first.Token).Started);
        Check.Throws(() => room.Join(OnlineIdentity.For(module)), "seat-unavailable");
        Check.Throws(() => room.Read("forged"), "invalid-identity");
        Check.Throws(() => room.Submit(first.Token, new(1, 0, second.Seat, new() { Kind = "draw" })), "identity-mismatch");
        var frame = room.Read(first.Token);
        Check.That(room.Submit(first.Token, new(1, frame.Revision, first.Seat, frame.Actions[0].Command)).Result.Accepted);
        Check.That(room.ExportAuthorityRecording().Replay().ExportLog().EntryCount > 0);
        room.Leave(first.Token);
        Check.That(room.Read(second.Token).Closed);
        Check.Throws(() => room.Read(first.Token), "invalid-identity");
    }

    public static void Ordering()
    {
        var module = TestModules.Load("samples/quick-cards");
        foreach (var mixed in new[] { false, true })
        {
            using var room = new OnlineSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, s => mixed && s.Id != "p1" ? "cpu" : "human"));
            var players = module.Initial.Seats.Where(s => !mixed || s.Id == "p1").Select(s => room.Join(OnlineIdentity.For(module), s.Id)).ToArray();
            var sequences = players.ToDictionary(p => p.Seat, _ => 1L);
            for (var i = 0; i < 100 && !room.Read(players[0].Token).View.Ended; i++)
            {
                var current = room.Read(players[0].Token).View.CurrentSeat;
                var player = players.FirstOrDefault(p => p.Seat == current);
                if (player == null) { Check.That(room.StepCpu()); continue; }
                var before = room.Read(player.Token);
                var proposal = new OnlineProposal(sequences[player.Seat], before.Revision, player.Seat, before.Actions[0].Command);
                Check.Throws(() => room.Submit(player.Token, proposal with { Sequence = proposal.Sequence + 1 }), "out-of-order");
                var result = room.Submit(player.Token, proposal);
                Check.That(result.Result.Accepted);
                var count = room.ExportAuthorityRecording().Replay().ExportLog().EntryCount;
                Check.That(Wire.Json(room.Submit(player.Token, proposal)) == Wire.Json(result));
                Check.That(room.ExportAuthorityRecording().Replay().ExportLog().EntryCount == count);
                Check.Throws(() => room.Submit(player.Token, proposal with { Command = new() { Kind = "forged" } }), "sequence-conflict");
                sequences[player.Seat]++;
                foreach (var p in players)
                {
                    var visible = room.Read(p.Token).View;
                    var expected = room.ExportAuthorityRecording().Replay().Project(p.Seat);
                    Check.That(Wire.Json(visible) == Wire.Json(expected));
                    Check.That(visible.Entities.Where(e => e.Owner != p.Seat && e.Container?.StartsWith("hand-") == true).All(e => e.Face == null));
                }
            }
            Check.That(room.Read(players[0].Token).View.Ended);
            var frames = room.CatchUp(players[0].Token, -1);
            Check.That(frames.Select(f => f.Revision).SequenceEqual(Enumerable.Range(0, frames.Count).Select(i => (long)i)));
            Check.That(Wire.Json(frames[^1]) == Wire.Json(room.Read(players[0].Token)));
            Check.That(!Wire.Json(frames).Contains("randomAfter"));
            var replica = new OnlineReplica(players[0].Seat);
            Check.That(replica.Apply(frames[^1]));
            Check.That(!replica.Apply(frames[0]) && !replica.Apply(frames[^1]));
            Check.That(Wire.Json(replica.Frame) == Wire.Json(frames[^1]));
        }
    }
}
