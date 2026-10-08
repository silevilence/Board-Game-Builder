using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

if (args.Length != 1) { Console.Error.WriteLine("Usage: NetworkClient <client.json>"); return 2; }
try
{
    var path = Path.GetFullPath(args[0]);
    var directory = Path.GetDirectoryName(path)!;
    var config = Wire.Read<ClientConfiguration>(File.ReadAllText(path));
    var module = GameRuntime.Load(Path.GetFullPath(config.Module, directory),config.Preset);
    var identity = OnlineIdentity.For(module);
    HttpClientHandler? handler = null;
    if (config.CertificateSha256 != null)
    {
        handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null &&
            string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData)), config.CertificateSha256, StringComparison.OrdinalIgnoreCase);
    }
    using var client = new OnlineClient(new(config.Server), config.RoomKey, handler);
    var leasePath = Path.GetFullPath(config.Credentials, directory);
    SeatLease lease;
    long nextSequence;
    long lastRevision = -1;
    IReadOnlyList<OnlineFrame> recovered = [];
    if (File.Exists(leasePath))
    {
        var saved = Wire.Read<SavedIdentity>(File.ReadAllText(leasePath));
        if (saved.Server != config.Server || saved.Identity != identity) throw new OnlineException("saved-identity-mismatch");
        lastRevision = saved.LastRevision;
        var resumed = await client.ResumeAsync(new(identity, saved.Lease.ResumeToken, lastRevision));
        lease = resumed.Lease; nextSequence = resumed.NextSequence;
        recovered = resumed.Frames;
    }
    else
    {
        lease = await client.JoinAsync(new(identity, config.Seat, config.Audience));
        nextSequence = 1;
    }
    void SaveIdentity()
    {
        var temporary = leasePath + ".tmp";
        File.WriteAllText(temporary, Wire.Json(new SavedIdentity(config.Server, identity, lease, lastRevision)));
        File.Move(temporary, leasePath, true);
    }
    SaveIdentity();
    Console.WriteLine(lease.Seat == SeatView.AudienceSeat ? "已进入公共观战。" : $"已加入席位 {lease.Seat}。");
    var replica = new OnlineReplica(lease.Seat);
    foreach (var snapshot in recovered) replica.Apply(snapshot);
    bool firstFrame = true;
    Task<string?>? input = config.Auto ? null : Task.Run(Console.ReadLine);
    while (true)
    {
        var frame = (await client.SendAsync(lease.Token, new(OnlineOperations.Frame))).Frame!;
        var changed = replica.Apply(frame);
        if (changed || firstFrame)
        {
            firstFrame = false;
            lastRevision = replica.Frame!.Revision;
            SaveIdentity();
            Console.WriteLine($"revision={frame.Revision} turn={frame.View.Turn} phase={frame.View.Phase} current={frame.View.CurrentSeat}");
            if (!config.Auto) foreach (var entity in frame.View.Entities) Console.WriteLine($"{entity.Id}: {entity.Face} {Wire.Json(entity.Properties)}");
            if (!config.Auto)
            {
                for (var i = 0; i < frame.Actions.Count; i++) Console.WriteLine($"{i + 1}. {frame.Actions[i].Label}");
                Console.WriteLine("选择动作编号；d 暂时断线，q 主动离开。");
            }
        }
        if (frame.Closed || frame.Fault != null) throw new OnlineException(frame.Fault ?? "session-closed");
        if (frame.View.Ended)
        {
            var recording = (await client.SendAsync(lease.Token, new(OnlineOperations.Recording))).Recording!;
            File.WriteAllText(Path.GetFullPath(config.Recording, directory), Wire.Json(recording));
            Console.WriteLine("对局结束：" + string.Join(",", frame.View.Winners));
            return 0;
        }
        var index = 0;
        if (!config.Auto)
        {
            // Poll and render state while input is pending, even outside this seat's turn.
            if (!input!.IsCompleted) { await Task.Delay(100); continue; }
            var choice = await input;
            if (choice is "d" or "q" || choice == null)
            {
                await client.SendAsync(lease.Token, new(choice == "q" ? OnlineOperations.Leave : OnlineOperations.Disconnect));
                if (choice == "q") File.Delete(leasePath);
                return 0;
            }
            input = Task.Run(Console.ReadLine);
            if (!int.TryParse(choice, out var number) || number < 1 || number > frame.Actions.Count) continue;
            index = number - 1;
        }
        if (!frame.Started || frame.Actions.Count == 0) { await Task.Delay(100); continue; }
        var proposal = new OnlineProposal(nextSequence, frame.Revision, lease.Seat, frame.Actions[index].Command);
        var receipt = (await client.SendAsync(lease.Token, new(OnlineOperations.Submit, Proposal: proposal))).Receipt!;
        nextSequence = receipt.NextSequence;
        if (!receipt.Result.Accepted) Console.WriteLine("操作未提交：" + receipt.Result.Reason);
    }
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }

public sealed record ClientConfiguration(string Module, string Server, string RoomKey, string Credentials, string Recording,
    string? Seat = null, bool Audience = false, bool Auto = false, string? CertificateSha256 = null,string? Preset=null);
public sealed record SavedIdentity(string Server, OnlineIdentity Identity, SeatLease Lease, long LastRevision = -1);
