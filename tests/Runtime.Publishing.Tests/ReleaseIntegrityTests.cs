using System.Security.Cryptography;
using BoardGame.Plugins;

internal static class ReleaseIntegrityTests
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"release-integrity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"content"));
        try
        {
            var file=Path.Combine(root,"content","定义.json");File.WriteAllText(file,"{}");
            var expected=NativeReleaseGuard.Describe([new("content/定义.json",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))]);
            NativeReleaseGuard.Verify(root,expected);
            File.WriteAllText(file,"{\"changed\":true}");
            File.WriteAllText(Path.Combine(root,"native-release.json"),NativeReleaseGuard.Describe([new("content/定义.json",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))]));
            Reject(()=>NativeReleaseGuard.Verify(root,expected),"differs");
            File.Delete(file);Reject(()=>NativeReleaseGuard.Verify(root,expected),"missing");
            Reject(()=>NativeReleaseGuard.Describe([new("content/../outside",new string('0',64))]),"");
            Console.WriteLine("PASS native release integrity: embedded identity, mutable sidecar rejection, content tamper, missing file and traversal");
        }
        finally{Directory.Delete(root,true);}
    }
    private static void Reject(Action action,string expected)
    {try{action();throw new Exception("Invalid release accepted");}catch(InvalidDataException error){if(!error.Message.Contains(expected,StringComparison.Ordinal))throw;}}
}
