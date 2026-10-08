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
        Check.Throws(() => room.Submit(first.Token, second.Seat, new() { Kind = "draw" }), "identity-mismatch");
        var frame = room.Read(first.Token);
        Check.That(room.Submit(first.Token, first.Seat, frame.Actions[0].Command).Accepted);
        Check.That(room.ExportAuthorityRecording().Replay().ExportLog().EntryCount > 0);
        room.Leave(first.Token);
        Check.That(room.Read(second.Token).Closed);
        Check.Throws(() => room.Read(first.Token), "invalid-identity");
    }
}
