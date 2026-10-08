using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class OnlineTransportTests
{
    private sealed class Host : IDisposable
    {
        public string Root { get; } = Path.GetFullPath("artifacts/m4/http-" + Guid.NewGuid().ToString("N"));
        public string Key { get; } = Guid.NewGuid().ToString("N");
        public Uri Url { get; }
        private readonly Process process;
        private readonly Task<string> stdout;
        private readonly Task<string> stderr;
        private bool stopped;
        public Host(string module, bool cpu)
        {
            Directory.CreateDirectory(Root);
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Url = new($"http://127.0.0.1:{port}/");
            var loaded = GameRuntime.Load(module);
            var config = new { module = Path.GetFullPath(module), listen = Url.ToString(), roomKey = Key,
                controllers = loaded.Initial.Seats.ToDictionary(s => s.Id, s => cpu && s.Id == "p3" ? "cpu" : "human"), output = "records" };
            var configPath = Path.Combine(Root, "server.json");
            File.WriteAllText(configPath, Wire.Json(config));
            process = Launch("src/Server/bin/Release/net10.0/Server.exe", configPath);
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(Root, "records/ready.json")) && !process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(50);
            if (!File.Exists(Path.Combine(Root, "records/ready.json"))) { Dispose(); throw new Exception("Server did not start: " + stderr.GetAwaiter().GetResult()); }
        }
        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            Directory.CreateDirectory(Path.Combine(Root, "records"));
            File.WriteAllText(Path.Combine(Root, "records/stop.request"), "stop");
            if (!process.WaitForExit(10000)) { process.Kill(true); process.WaitForExit(); }
            File.WriteAllText(Path.Combine(Root, "server.stdout.txt"), stdout.GetAwaiter().GetResult());
            File.WriteAllText(Path.Combine(Root, "server.stderr.txt"), stderr.GetAwaiter().GetResult());
            process.Dispose();
        }
    }

    private static Process Launch(string executable, string config)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(config);
        return Process.Start(start) ?? throw new Exception("Could not start " + executable);
    }

    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        foreach (var sample in new[] { "quick-cards", "sealed-auction" })
        {
            var module = GameRuntime.Load("samples/" + sample);
            using var host = new Host("samples/" + sample, true);
            using var client = new OnlineClient(host.Url, host.Key);
            using var other = new OnlineClient(host.Url, host.Key);
            using var denied = new OnlineClient(host.Url, "wrong-key");
            var identity = OnlineIdentity.For(module);
            Check.Throws(() => denied.JoinAsync(new(identity)).GetAwaiter().GetResult(), "room-access-denied");
            Check.Throws(() => client.JoinAsync(new(identity with { Fingerprint = "wrong-version" })).GetAwaiter().GetResult(), "version-mismatch");
            var p1 = await client.JoinAsync(new(identity, "p1"));
            var p2 = await other.JoinAsync(new(identity, "p2"));
            var spectator = await other.JoinAsync(new(identity, Audience: true));
            Check.Throws(() => other.SendAsync(spectator.Token, new("submit", Proposal: new(1, 0, "", new()))).GetAwaiter().GetResult(), "audience-read-only");
            Check.Throws(() => client.SendAsync(p1.Token, new("submit", Proposal: new(1, 0, "p2", new()))).GetAwaiter().GetResult(), "identity-mismatch");
            Check.Throws(() => client.SendAsync(p1.Token, new("authority-recording")).GetAwaiter().GetResult(), "unknown-operation");
            var players = new[] { p1, p2 };
            var next = new long[] { 1, 1 };
            var resumed = false;
            for (var i = 0; i < 300; i++)
            {
                var current = (await client.SendAsync(players[0].Token, new("frame"))).Frame!;
                if (current.View.Ended) break;
                Check.That(current.Fault == null, current.Fault ?? "");
                var index = Array.FindIndex(players, p => p.Seat == current.View.CurrentSeat);
                if (index < 0) { await Task.Delay(100); continue; }
                var p = players[index];
                var frame = (await client.SendAsync(p.Token, new("frame"))).Frame!;
                Check.That(frame.Actions.Count > 0);
                var proposal = new OnlineProposal(next[index], frame.Revision, p.Seat, frame.Actions[0].Command);
                Check.Throws(() => client.SendAsync(p.Token, new("submit", Proposal: proposal with { Sequence = proposal.Sequence + 1 })).GetAwaiter().GetResult(), "out-of-order");
                var receipt = (await client.SendAsync(p.Token, new("submit", Proposal: proposal))).Receipt!;
                next[index] = receipt.NextSequence;
                // Simulate lost acknowledgement, then delayed duplicate delivery.
                await Task.Delay(20);
                var duplicate = (await client.SendAsync(p.Token, new("submit", Proposal: proposal))).Receipt!;
                Check.That(Wire.Json(duplicate) == Wire.Json(receipt));
                if (!resumed)
                {
                    await client.SendAsync(p.Token, new("disconnect"));
                    var recovery = await other.ResumeAsync(new(identity, p.ResumeToken, frame.Revision));
                    players[index] = recovery.Lease;
                    Check.That(recovery.NextSequence == next[index]);
                    Check.That(recovery.Frames.Count > 0);
                    Check.Throws(() => client.SendAsync(p.Token, new("frame")).GetAwaiter().GetResult(), "invalid-identity");
                    resumed = true;
                }
            }
            var final = (await client.SendAsync(players[0].Token, new("frame"))).Frame!;
            Check.That(final.View.Ended && final.Fault == null);
            foreach (var player in players.Append(spectator))
            {
                var record = (await client.SendAsync(player.Token, new("recording"))).Recording!;
                Check.That(record.Replay().Frame!.View.Ended);
                if (player.Seat == "") Check.That(record.Frames.All(f => f.View.Secret?.OwnValue == null &&
                    f.View.Entities.All(e => e.Container?.StartsWith("hand-") != true)));
                File.WriteAllText(Path.Combine(host.Root, "projection-" + (player.Seat == "" ? "audience" : player.Seat) + ".json"), Wire.Json(record));
            }
            File.WriteAllText(Path.Combine(host.Root, "result.json"), Wire.Json(new { success = true, sample, resumed, revision = final.Revision }));
            host.Dispose();
            var authority = Wire.Read<SessionRecording>(File.ReadAllText(Path.Combine(host.Root, "records/authority.json"))).Replay();
            foreach (var player in players)
            {
                var record = Wire.Read<OnlineRecording>(File.ReadAllText(Path.Combine(host.Root, "projection-" + player.Seat + ".json")));
                Check.That(Wire.Json(record.Replay().Frame!.View) == Wire.Json(authority.Project(player.Seat)));
            }
        }
    }

    public static void Processes()
    {
        using var host = new Host("samples/quick-cards", false);
        var clients = new List<(Process Process, Task<string> Output, Task<string> Error)>();
        try
        {
            foreach (var seat in new[] { "p1", "p2" })
            {
                var config = Path.Combine(host.Root, seat + ".json");
                File.WriteAllText(config, Wire.Json(new { module = Path.GetFullPath("samples/quick-cards"), server = host.Url.ToString(), roomKey = host.Key,
                    credentials = seat + "-identity.json", recording = seat + "-recording.json", seat, auto = true }));
                var process = Launch("src/NetworkClient/bin/Release/net10.0/NetworkClient.exe", config);
                clients.Add((process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
            }
            foreach (var client in clients)
            {
                Check.That(client.Process.WaitForExit(60000), "Client timed out");
                Check.That(client.Process.ExitCode == 0, client.Error.GetAwaiter().GetResult());
            }
            foreach (var seat in new[] { "p1", "p2" })
                Check.That(Wire.Read<OnlineRecording>(File.ReadAllText(Path.Combine(host.Root, seat + "-recording.json"))).Replay().Frame!.View.Ended);
            File.WriteAllText(Path.Combine(host.Root, "process-result.json"), Wire.Json(new { success = true, clients = 2, server = 1 }));
        }
        finally
        {
            foreach (var client in clients) { if (!client.Process.HasExited) client.Process.Kill(true); client.Process.Dispose(); }
        }
    }

    public static void Faults() => FaultsAsync().GetAwaiter().GetResult();
    private static async Task FaultsAsync()
    {
        var module = GameRuntime.Load("samples/quick-cards");
        var broken = TestModules.Rebuild(module with { Cpu = "return {decide=function(c) error('private diagnostic marker') end}" });
        using (var room = new OnlineSession(broken, 1, broken.Initial.Seats.ToDictionary(s => s.Id, _ => "cpu")))
        {
            var viewer = room.Watch(OnlineIdentity.For(broken));
            Check.That(!room.StepCpu());
            var frame = room.Read(viewer.Token);
            Check.That(frame.Fault == "cpu-failure");
            Check.That(!Wire.Json(room.ExportPlayerRecording(viewer.Token)).Contains("private diagnostic marker"));
            Check.That(room.ExportAuthorityRecording().Replay().ExportLog().EntryCount == 0);
        }
        using var host = new Host("samples/quick-cards", false);
        using var client = new OnlineClient(host.Url, host.Key);
        var player = await client.JoinAsync(new(OnlineIdentity.For(module), "p1"));
        var second = await client.JoinAsync(new(OnlineIdentity.For(module), "p2"));
        using var raw = new HttpClient { BaseAddress = host.Url };
        raw.DefaultRequestHeaders.Add("X-Room-Key", host.Key);
        raw.DefaultRequestHeaders.Authorization = new("Bearer", player.Token);
        var malformed = await raw.PostAsync("rpc", new StringContent("{invalid"));
        Check.That(malformed.StatusCode == HttpStatusCode.BadRequest);
        Check.Throws(() => client.SendAsync("forged", new("frame")).GetAwaiter().GetResult(), "invalid-identity");
        var frameBefore = (await client.SendAsync(player.Token, new("frame"))).Frame!;
        var stale = (await client.SendAsync(player.Token, new("submit", Proposal: new(1, frameBefore.Revision - 1, player.Seat, frameBefore.Actions[0].Command)))).Receipt!;
        Check.That(!stale.Result.Accepted && stale.Result.Reason == "stale-revision");
        Check.That(Wire.Json(stale.Frame.View) == Wire.Json(frameBefore.View));
        await client.SendAsync(second.Token, new("leave"));
        Check.That((await client.SendAsync(player.Token, new("frame"))).Frame!.Closed);
        host.Dispose();
        Check.Throws(() => client.SendAsync(player.Token, new("frame")).GetAwaiter().GetResult(), "server-unreachable");
        File.WriteAllText(Path.Combine(host.Root, "fault-result.json"), Wire.Json(new { success = true, malformed = true, stale = true, leave = true, unreachable = true, cpuFault = true }));
    }
}
