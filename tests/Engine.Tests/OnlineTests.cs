using BoardGame.Kernel;
using BoardGame.Networking;

namespace Engine.Tests;

public static class OnlineTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }
    public static void Reconnection()
    {
        var module = TestModules.Load("samples/quick-cards");
        foreach (var cpuTurn in new[] { false, true })
        {
            var clock = new Clock();
            using var room = new OnlineSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, s => s.Id == "p1" ? "human" : "cpu"), time: clock);
            var player = room.Join(OnlineIdentity.For(module));
            var frame = room.Read(player.Token);
            var proposal = new OnlineProposal(1, frame.Revision, player.Seat, frame.Actions[0].Command);
            long next = 1;
            if (cpuTurn)
            {
                while (room.Read(player.Token).View.CurrentSeat == player.Seat)
                {
                    var current = room.Read(player.Token);
                    proposal = new(next++, current.Revision, player.Seat, current.Actions[0].Command);
                    Check.That(room.Submit(player.Token, proposal).Result.Accepted);
                }
            }
            room.Disconnect(player.Token);
            if (cpuTurn) Check.That(room.StepCpu());
            clock.Advance(300); room.Maintain(); // Default wait never replaces the human.
            Check.Throws(() => room.Read(player.Token), "disconnected");
            Check.Throws(() => room.Resume(OnlineIdentity.For(module), "forged", -1), "invalid-identity");
            var restored = room.Resume(OnlineIdentity.For(module), player.ResumeToken, frame.Revision);
            Check.That(restored.Lease.Token != player.Token && restored.NextSequence == next);
            Check.Throws(() => room.Read(player.Token), "invalid-identity");
            Check.That(Wire.Json(room.Read(restored.Lease.Token).View) == Wire.Json(room.ExportAuthorityRecording().Replay().Project(player.Seat)));
            if (cpuTurn)
            {
                var before = room.ExportAuthorityRecording().Replay().ExportLog().EntryCount;
                Check.That(room.Submit(restored.Lease.Token, proposal).Result.Accepted);
                Check.That(room.ExportAuthorityRecording().Replay().ExportLog().EntryCount == before);
            }
            else Check.That(room.Submit(restored.Lease.Token, proposal).Result.Accepted);
            room.Leave(restored.Lease.Token);
            Check.Throws(() => room.Resume(OnlineIdentity.For(module), player.ResumeToken, -1), "invalid-identity");
        }
        foreach (var policy in new[] { DisconnectPolicy.Cpu, DisconnectPolicy.EndSession })
        {
            var clock = new Clock();
            using var room = new OnlineSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, s => s.Id == "p1" ? "human" : "cpu"),
                new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), policy), clock);
            var player = room.Join(OnlineIdentity.For(module));
            clock.Advance(16); room.Maintain();
            Check.That(room.StepCpu() == (policy == DisconnectPolicy.Cpu));
            var events = room.DrainLifecycleEvents();
            Check.That(events.Any(e => e.Code == "connection-timeout"));
            Check.That(events.Any(e => e.Code == (policy == DisconnectPolicy.Cpu ? "cpu-takeover" : "timeout-ended-session")));
            var restored = room.Resume(OnlineIdentity.For(module), player.ResumeToken, -1);
            Check.That(restored.Frames[^1].Closed == (policy == DisconnectPolicy.EndSession));
        }
    }
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
                if (player == null)
                {
                    Check.That(room.StepCpu());
                    Check.That(room.Read(players[0].Token).Event!.Actor == current);
                    continue;
                }
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

    public static void Audience()
    {
        var module = TestModules.Load("samples/quick-cards");
        using var room = new OnlineSession(module, 42, module.Initial.Seats.ToDictionary(s => s.Id, _ => "human"));
        var first = room.Join(OnlineIdentity.For(module), "p1");
        var second = room.Join(OnlineIdentity.For(module), "p2");
        var frame = room.Read(first.Token);
        Check.That(room.Submit(first.Token, new(1, frame.Revision, first.Seat, frame.Actions[0].Command)).Result.Accepted);
        var observer = room.Watch(OnlineIdentity.For(module));
        var view = room.Read(observer.Token);
        Check.That(view.View.Seat == "" && view.Actions.Count == 0 && view.Revision > 0);
        Check.That(view.View.Entities.All(e => e.Container?.StartsWith("hand-") != true));
        Check.Throws(() => room.Submit(observer.Token, new(1, view.Revision, "", new() { Kind = "draw" })), "audience-read-only");
        Check.That(Wire.Json(room.CatchUp(observer.Token, -1)[^1]) == Wire.Json(view));
        foreach (var player in new[] { first, second, observer })
        {
            var recording = room.ExportPlayerRecording(player.Token);
            Check.That(Wire.Json(recording.Replay().Frame) == Wire.Json(room.Read(player.Token)));
            Check.That(!Wire.Json(recording).Contains("randomAfter") && !Wire.Json(recording).Contains("resumeToken"));
            var malformed = recording with { Frames = recording.Frames.Reverse().ToArray() };
            Check.Throws(() => malformed.Replay(), "invalid-recording");
        }
        room.Leave(observer.Token);
        Check.That(!room.Read(first.Token).Closed);
    }

    public static void Secrets()
    {
        var module = TestModules.Load("samples/sealed-auction");
        foreach (var deadline in new[] { false, true })
        {
            var clock = new Clock();
            using var room = new OnlineSession(module, 7, module.Initial.Seats.ToDictionary(s => s.Id, s => s.Id == "p3" ? "cpu" : "human"),
                new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2), Timers: [new("p1", "tick", TimeSpan.FromSeconds(1))]), clock);
            var first = room.Join(OnlineIdentity.For(module), "p1");
            var second = room.Join(OnlineIdentity.For(module), "p2");
            var observer = room.Watch(OnlineIdentity.For(module));
            long n1 = 1, n2 = 1;
            OnlineReceipt Send(SeatLease lease, long sequence, Command command) => room.Submit(lease.Token, new(sequence, room.Read(lease.Token).Revision, lease.Seat, command));
            Check.That(Send(first, n1++, new() { Kind = "open" }).Result.Accepted);
            Check.That(!Send(first, n1++, new() { Kind = "tick" }).Result.Accepted);
            Check.That(room.Read(first.Token).View.Secret!.Remaining == 12);
            var bid = new OnlineProposal(n2++, room.Read(second.Token).Revision, second.Seat, new() { Kind = "bid", Value = 4 });
            Check.That(room.Submit(second.Token, bid).Result.Accepted);
            room.Disconnect(second.Token);
            second = room.Resume(OnlineIdentity.For(module), second.ResumeToken, -1).Lease;
            Check.That(room.Submit(second.Token, bid).Result.Accepted);
            Check.That(room.Read(first.Token).View.Secret!.OwnValue == null && room.Read(observer.Token).View.Secret!.Revealed == null);
            Check.That(!Send(second, n2++, new() { Kind = "bid", Value = 5 }).Result.Accepted);
            if (deadline)
                for (var i = 0; i < 12; i++) { clock.Advance(1); room.Maintain(); }
            else
            {
                Check.That(Send(first, n1++, new() { Kind = "bid", Value = 2 }).Result.Accepted);
                Check.That(room.StepCpu());
            }
            Check.That(Send(first, n1++, new() { Kind = "close" }).Result.Accepted);
            Check.That(!Send(second, n2++, new() { Kind = "bid", Value = 3 }).Result.Accepted);
            foreach (var p in new[] { first, second, observer })
                Check.That(room.Read(p.Token).View.Secret!.Revealed == null);
            Check.That(Send(first, n1++, new() { Kind = "reveal" }).Result.Accepted);
            Check.That(room.Read(observer.Token).View.Secret!.Revealed!["p2"] == 4);
            Check.That(Send(first, n1++, new() { Kind = "settle" }).Result.Accepted);
            Check.That(room.Read(first.Token).View.Ended);
            Check.That(Wire.Json(room.ExportAuthorityRecording().Replay().Project("p1")) == Wire.Json(room.Read(first.Token).View));
            foreach (var frame in room.ExportPlayerRecording(observer.Token).Frames.Where(f => f.View.Secret?.Status is "collecting" or "closed"))
                Check.That(frame.View.Secret!.OwnValue == null && frame.View.Secret.Revealed == null);
        }
    }

    public static void Review()
    {
        const string rules = """
        return {
          actions=function(c) return {
            {id='timer',label='timer',command={kind='set',entity='card',property='value',value=1}},
            {id='manual',label='manual',command={kind='set',entity='card',property='value',value=2}}} end,
          decide=function(c) return {allowed=true,reason='',commands={c.command}} end
        }
        """;
        var module = TestModules.LuaGame(CommandTests.Initial(), rules, "return {decide=function(c) return c.actions[1].command end}");
        var clock = new Clock();
        using var room = new OnlineSession(module, 1, module.Initial.Seats.ToDictionary(s => s.Id, _ => "human"),
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), Timers: [new("p1", "timer", TimeSpan.FromSeconds(1))], MaxObservers: 1), clock);
        var first = room.Join(OnlineIdentity.For(module), "p1");
        _ = room.Join(OnlineIdentity.For(module), "p2");
        var watcher = room.Watch(OnlineIdentity.For(module));
        Check.Throws(() => room.Watch(OnlineIdentity.For(module)), "audience-limit");
        var frame = room.Read(first.Token);
        Check.That(frame.Actions.Count == 1 && frame.Actions[0].Id == "manual");
        Check.That(room.Submit(first.Token, new(1, frame.Revision, "p1", frame.Actions[0].Command)).Result.Accepted);
        Check.That(!room.Submit(first.Token, new(2, room.Read(first.Token).Revision, "p1", frame.Actions[0].Command with { Value = 1 })).Result.Accepted);
        clock.Advance(11); room.Maintain();
        var replacement = room.Watch(OnlineIdentity.For(module));
        Check.Throws(() => room.Resume(OnlineIdentity.For(module), watcher.ResumeToken, -1), "invalid-identity");
        Check.That(room.Read(replacement.Token).View.Entities.Single(e => e.Id == "card").Properties["value"] == 1);
    }
}
