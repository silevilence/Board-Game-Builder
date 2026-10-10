using System.Text;

namespace BoardGame.Authoring;

internal static class AtomicTextFile
{
    public static void Write(string target, string text, bool replace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staging = target + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(Encoding.UTF8.GetBytes(text)); stream.Flush(true); }
            File.Move(staging, target, replace);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
