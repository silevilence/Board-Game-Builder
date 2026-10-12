using System.Security.Cryptography;
using System.Text.Json;

namespace BoardGame.Launcher;

internal sealed record LegacyReceipt(string ArchiveHash,string[] Files);
internal static class LegacyOwnership
{
    internal static LegacyReceipt Read(ContentStore store,ProductManifest manifest)
    {
        var path=ProductManifest.SafePath(store.Root,"receipts/"+manifest.Digest+".json");
        if(!File.Exists(path) || new FileInfo(path).Length>4*1024*1024) throw new InvalidDataException("旧包缺少安装文件记录，请用原始 ZIP 再次导入以核验身份。");
        var receipt=JsonSerializer.Deserialize<LegacyReceipt>(File.ReadAllBytes(path))??throw new InvalidDataException("旧包安装文件记录损坏。");
        if(!ProductManifest.IsHash(receipt.ArchiveHash) || receipt.Files.Length is 0 or >20000 || receipt.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=receipt.Files.Length) throw new InvalidDataException("无效旧包安装记录。");
        foreach(var file in receipt.Files) ProductManifest.Relative(file); return receipt;
    }
    internal static void Record(ContentStore store,ProductManifest manifest,string hash,IEnumerable<string> files)
    {
        var path=ProductManifest.SafePath(store.Root,"receipts/"+manifest.Digest+".json");var names=files.Order(StringComparer.Ordinal).ToArray();
        if(File.Exists(path)) {var old=Read(store,manifest);if(old.ArchiveHash!=hash || !old.Files.SequenceEqual(names)) throw new InvalidDataException("相同旧版清单对应不同 ZIP 内容，拒绝覆盖。");return;}
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temporary,JsonSerializer.SerializeToUtf8Bytes(new LegacyReceipt(hash,names)));File.Move(temporary,path);}
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
    internal static async Task VerifyExisting(ContentStore store,ProductManifest manifest,string directory,Dictionary<string,System.IO.Compression.ZipArchiveEntry> archive,string hash,CancellationToken cancellation)
    {
        foreach(var entry in archive.Values)
        {
            cancellation.ThrowIfCancellationRequested();var path=ProductManifest.SafePath(directory,entry.FullName);
            await using var input=entry.Open();var expected=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,cancellation));ProductManifest.VerifyFile(path,entry.Length,expected);
        }
        Record(store,manifest,hash,archive.Keys);
    }
}
