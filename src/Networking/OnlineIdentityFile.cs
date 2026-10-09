using BoardGame.Kernel;

namespace BoardGame.Networking;

public static class OnlineIdentityFile
{
    public static async Task SaveAsync(string path,OnlineSavedIdentity identity,CancellationToken cancellation=default)
    {
        path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".tmp";
        File.WriteAllText(temporary,Wire.Json(identity));
        await Retry(()=>File.Move(temporary,path,true),cancellation).ConfigureAwait(false);
    }
    public static Task DeleteAsync(string path,CancellationToken cancellation=default)=>Retry(()=>File.Delete(path),cancellation);
    private static async Task Retry(Action operation,CancellationToken cancellation)
    {
        // Windows readers can briefly deny replacement/deletion even in a writable directory.
        // Keep atomic replacement and bound retries; permanent denial still surfaces.
        for(var attempt=0;;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            try {operation();return;}
            catch(Exception error) when(attempt<5 && OperatingSystem.IsWindows() &&
                error is IOException or UnauthorizedAccessException && (error.HResult & 0xffff) is 5 or 32 or 33)
            {await Task.Delay(20<<attempt,cancellation).ConfigureAwait(false);}
        }
    }
}
