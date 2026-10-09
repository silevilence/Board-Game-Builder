using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class OnlineIdentityPersistenceTests
{
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        if(!OperatingSystem.IsWindows())return;
        var module=GameRuntime.Load("samples/quick-cards");
        using var host=new OnlineTransportTests.Host("samples/quick-cards",false);
        var path=Path.Combine(host.Root,"identity.json");
        using var player=new OnlinePlayerSession(module,new(host.Url.ToString(),host.Key,"p1"),path);
        await player.ConnectAsync();
        var before=Wire.Read<OnlineSavedIdentity>(File.ReadAllText(path));
        // A reader without delete sharing temporarily prevents atomic replacement on Windows.
        var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var release=Task.Run(async ()=> {await Task.Delay(150);reader.Dispose();});
        try {await player.RefreshAsync();}
        finally {await release;reader.Dispose();}
        Check.That(player.Connected,"A transient identity-file lock disconnected the player");
        var after=Wire.Read<OnlineSavedIdentity>(File.ReadAllText(path));
        Check.That(after.Lease==before.Lease && after.LastRevision==player.Frame!.Revision);
        File.SetAttributes(path,FileAttributes.ReadOnly);
        try
        {
            var failed=false;
            try {await player.RefreshAsync();}
            catch(UnauthorizedAccessException) {failed=true;}
            Check.That(failed && !player.Connected,"Persistent write denial must still be reported");
        }
        finally {File.SetAttributes(path,FileAttributes.Normal);}
        await player.ConnectAsync();
        Check.That(player.Connected && player.Frame!.View.Seat=="p1","The saved identity must remain recoverable");
        reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        release=Task.Run(async ()=> {await Task.Delay(150);reader.Dispose();});
        try {await player.DisconnectAsync(true);}
        finally {await release;reader.Dispose();}
        Check.That(!File.Exists(path) && !player.Connected,"Leaving must remove the saved identity after a temporary lock");
    }
}
