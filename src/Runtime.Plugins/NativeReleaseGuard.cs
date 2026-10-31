using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

public sealed record NativeReleaseFile(string Path,string Sha256);
internal sealed record NativeReleaseIntegrity(int Format,NativeReleaseFile[] Files);

/// <summary>The expected content/native library hashes are embedded in the compiled entry.
/// An editable sidecar inventory cannot authorize different runtime code or game content.</summary>
public static class NativeReleaseGuard
{
    public static string Describe(IEnumerable<NativeReleaseFile> files)
    {
        var integrity=new NativeReleaseIntegrity(1,files.OrderBy(f=>f.Path,StringComparer.Ordinal).ToArray());Validate(integrity);
        return JsonSerializer.Serialize(integrity,ReleaseIntegrityJson.Default.NativeReleaseIntegrity);
    }
    public static void Verify(string directory,string expected)
    {
        var integrity=JsonSerializer.Deserialize(expected,ReleaseIntegrityJson.Default.NativeReleaseIntegrity) ?? throw new InvalidDataException("release.integrity: missing manifest");
        Validate(integrity);var root=System.IO.Path.GetFullPath(directory);
        foreach(var file in integrity.Files)
        {
            var path=System.IO.Path.Combine(root,file.Path);
            try
            {
                using var stream=File.OpenRead(path);
                if(Convert.ToHexStringLower(SHA256.HashData(stream))!=file.Sha256)
                    throw new InvalidDataException("release.integrity: content differs: "+file.Path);
            }
            catch(Exception error)when(error is FileNotFoundException or DirectoryNotFoundException)
            {throw new InvalidDataException("release.integrity: missing required file: "+file.Path,error);}
        }
    }
    private static void Validate(NativeReleaseIntegrity value)
    {
        if(value.Format!=1 || value.Files is not {Length:>0 and <=8192})throw new InvalidDataException("release.integrity: unsupported format or file count");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in value.Files)
        {
            if(file==null || file.Path==null)throw new InvalidDataException("release.integrity: invalid file");
            ContentPath.Validate(file.Path.StartsWith("content/",StringComparison.Ordinal)?file.Path[8..]:file.Path);
            if(!paths.Add(file.Path) || file.Sha256 is not {Length:64} || file.Sha256.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new InvalidDataException("release.integrity: duplicate path or invalid hash: "+file.Path);
        }
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(NativeReleaseIntegrity))]
internal partial class ReleaseIntegrityJson:JsonSerializerContext;
