using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoardGame.Launcher;

public sealed record InstallationResult(InstalledVersion Installation,long DownloadedBytes,int ReusedObjects,string[] Components);

/// <summary>Commit a verified product directory and its registry entry; recover incomplete attempts under the store lock.</summary>
public sealed class InstallationService(InstallationRegistry registry,HttpClient client)
{
    private sealed record Journal(string Version,string ManifestHash);
    public async Task<InstallationResult> Install(CatalogVersion version,string mode,IProgress<TransferProgress>? progress=null,CancellationToken cancellation=default)
    {
        var manifest=RuntimePolicy.Select(version,mode).ReadManifest();
        if(manifest.Legacy) throw new InvalidOperationException("V0.1.0 请使用完整 ZIP 和配套校验信息导入。");
        return await Execute(manifest,null,null,progress,cancellation);
    }
    public async Task<InstallationResult> Import(string archive,string checksum,IProgress<TransferProgress>? progress=null,CancellationToken cancellation=default)
    {
        archive=Path.GetFullPath(archive); checksum=Path.GetFullPath(checksum);
        if(new FileInfo(checksum).Length>1024) throw new InvalidDataException("校验信息过大。");
        var expected=(await File.ReadAllTextAsync(checksum,cancellation)).Trim();
        if(expected.Length!=64+2+Path.GetFileName(archive).Length || expected[64..]!="  "+Path.GetFileName(archive) || !ProductManifest.IsHash(expected[..64])) throw new InvalidDataException("配套校验信息必须绑定准确的 ZIP 文件名与 SHA-256。");
        // Hold a read-only handle through extraction so a verified archive cannot be replaced in between.
        using var input=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
        var hash=Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(input,cancellation));
        if(hash!=expected[..64]) throw new InvalidDataException("ZIP SHA-256 校验失败。"); input.Position=0;
        using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        var entries=ValidateArchive(zip);
        if(!entries.TryGetValue("toolkit.json",out var entry) || entry.Length>16*1024*1024) throw new InvalidDataException("ZIP 缺少版本清单或清单过大。");
        using var data=new MemoryStream(); await CopyBounded(entry.Open(),data,entry.Length,cancellation);
        var manifest=ProductManifest.Parse(data.ToArray());
        if(!manifest.Legacy && !entries.Keys.Order(StringComparer.Ordinal).SequenceEqual(manifest.Files.Select(f=>f.Storage).Append("toolkit.json").Distinct().Order(StringComparer.Ordinal))) throw new InvalidDataException("ZIP 与清单的完整文件集合不符。");
        return await Execute(manifest,entries,hash,progress,cancellation);
    }
    private async Task<InstallationResult> Execute(ProductManifest manifest,Dictionary<string,ZipArchiveEntry>? archive,string? archiveHash,IProgress<TransferProgress>? progress,CancellationToken cancellation)
    {
        var store=new ContentStore(registry.Read().StorageRoot);
        using var operation=await store.Begin(cancellation);InstallationRepair.RecoverLocked(registry,store); InstallationMaintenance.RecoverLocked(registry,store);RecoverLocked(registry,store);
        ManifestCache.Write(registry.Home,manifest);
        var existing=registry.Read().Installations.SingleOrDefault(i=>i.Version==manifest.Version);
        if(existing!=null)
        {
            if(existing.ManifestHash!=manifest.Digest || existing.Mode!=manifest.Mode) throw new InvalidOperationException("此版本已以不同内容或运行时模式安装，请先明确卸载原安装。");
            if(registry.Inspect(existing).State!="available") throw new InvalidOperationException("原安装损坏，请使用修复功能。");
            if(manifest.Legacy && archive!=null) await LegacyOwnership.VerifyExisting(store,manifest,existing.Directory,archive,archiveHash!,cancellation);
            _=RuntimePolicy.Resolve(manifest,existing.Directory); return new(existing,0,manifest.Files.Select(f=>f.Sha256).Distinct().Count(),[]);
        }
        var destination=ProductManifest.SafePath(store.Root,"versions/"+manifest.Version);
        if(Directory.Exists(destination)) throw new InvalidOperationException("目标版本目录未登记且已存在，不能覆盖。");
        _=RuntimePolicy.Resolve(manifest);
        var transaction="transactions/"+Guid.NewGuid().ToString("N"); var stage=ProductManifest.SafePath(store.Root,transaction+"/product");
        Directory.CreateDirectory(stage);
        var journalPath=ProductManifest.SafePath(store.Root,transaction+"/journal.json");
        var journalTemporary=journalPath+".tmp";
        using(var file=new FileStream(journalTemporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,new Journal(manifest.Version,manifest.Digest));file.Flush(true);}
        File.Move(journalTemporary,journalPath);
        var moved=false; var committed=false; var reused=manifest.Legacy?0:store.Plan(manifest).ReusedObjects;
        try
        {
            if(manifest.Legacy)
            {
                if(archive==null) throw new InvalidOperationException("旧包必须从完整归档导入。");
                store.CheckSpace(archive.Values.Sum(e=>e.Length));
                foreach(var entry in archive.Values.Where(e=>e.FullName!="toolkit.json"))
                {
                    cancellation.ThrowIfCancellationRequested(); var path=ProductManifest.SafePath(stage,entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                    await CopyBounded(entry.Open(),output,entry.Length,cancellation);
                }
            }
            else
            {
                if(archive==null) await operation.Fetch(manifest,client,progress,cancellation);
                else foreach(var file in manifest.Files.DistinctBy(f=>f.Sha256))
                {
                    cancellation.ThrowIfCancellationRequested(); var entry=archive[file.Storage];
                    if(entry.Length!=file.Size) throw new InvalidDataException("归档文件大小与清单不符："+file.Storage);
                    await using var input=entry.Open(); await operation.Add(file,input,cancellation);
                }
                progress?.Report(new("install",null,operation.DownloadedBytes,operation.DownloadedBytes));
                foreach(var file in manifest.Files)
                {
                    cancellation.ThrowIfCancellationRequested(); var path=ProductManifest.SafePath(stage,file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    ContentStore.Link(path,store.ObjectPath(file.Sha256));
                }
            }
            progress?.Report(new("verify",null,operation.DownloadedBytes,operation.DownloadedBytes));
            manifest.VerifyDirectory(stage); _=RuntimePolicy.Resolve(manifest,stage);
            if(manifest.Legacy) LegacyOwnership.Record(store,manifest,archiveHash!,archive!.Keys);
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllBytes(Path.Combine(stage,"toolkit.json"),manifest.Bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(stage,destination); moved=true;
            var item=registry.Register(destination,true); committed=true; operation.Commit();
            StoreFiles.DeleteTree(store.Root,transaction);
            return new(item,operation.DownloadedBytes,reused,operation.RequestedComponents.ToArray());
        }
        catch
        {
            if(!committed)
            {
                try
                {
                    using var registryGate=registry.Lock();
                    if(moved && registry.Read().Installations.Any(i=>i.Directory.Equals(destination,StringComparison.OrdinalIgnoreCase))) operation.Commit();
                    else if(moved)
                    {
                        InstallationMaintenance.RequireStopped(destination,manifest,registry.Read().Installations.Select(i=>i.Directory));
                        InstallationMaintenance.RequireOnlyOwnedFiles(store,destination,manifest);
                        StoreFiles.DeleteTree(store.Root,"versions/"+manifest.Version);
                    }
                    StoreFiles.DeleteTree(store.Root,transaction);
                }
                catch {operation.Commit();throw new IOException("安装失败且清理尚未完成；事务记录已保留，下次安装会恢复，请勿手动改动共享文件。");}
            }
            throw;
        }
    }
    public async Task Recover(CancellationToken cancellation=default)
    {
        var store=new ContentStore(registry.Read().StorageRoot); using var operation=await store.Begin(cancellation);InstallationRepair.RecoverLocked(registry,store);InstallationMaintenance.RecoverLocked(registry,store); RecoverLocked(registry,store); operation.Commit();
    }
    internal static void RecoverLocked(InstallationRegistry registry,ContentStore store)
    {
        using var registryGate=registry.Lock();
        var root=ProductManifest.SafePath(store.Root,"transactions"); if(!Directory.Exists(root)) return;
        foreach(var folder in Directory.EnumerateDirectories(root))
        {
            var name=Path.GetFileName(folder); if(!Regex.IsMatch(name,@"\A[0-9a-f]{32}\z")) throw new InvalidDataException("未知安装事务目录。");
            var relative="transactions/"+name; var journalPath=ProductManifest.SafePath(store.Root,relative+"/journal.json");
            if(File.Exists(journalPath))
            {
                if(new FileInfo(journalPath).Length>4096) throw new InvalidDataException("安装事务记录过大。");
                var journal=JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(journalPath))??throw new InvalidDataException("损坏的安装事务记录。");
                if(ProductManifest.NormalizeVersion(journal.Version)!=journal.Version || !ProductManifest.IsHash(journal.ManifestHash)) throw new InvalidDataException("无效安装事务身份。");
                var destination=ProductManifest.SafePath(store.Root,"versions/"+journal.Version);
                if(Directory.Exists(destination))
                {
                    var installed=registry.Read().Installations.FirstOrDefault(i=>i.Directory.Equals(destination,StringComparison.OrdinalIgnoreCase));
                    if(installed==null)
                    {
                        var manifest=ProductManifest.Read(ProductManifest.SafePath(destination,"toolkit.json"));
                        if(manifest.Digest!=journal.ManifestHash) throw new InvalidDataException("恢复时发现目标版本身份冲突，已保留目录。");
                        InstallationMaintenance.RequireStopped(destination,manifest,registry.Read().Installations.Select(i=>i.Directory));
                        InstallationMaintenance.RequireOnlyOwnedFiles(store,destination,manifest);
                        StoreFiles.DeleteTree(store.Root,"versions/"+journal.Version);
                    }
                    else if(installed.ManifestHash!=journal.ManifestHash) throw new InvalidDataException("恢复时安装登记冲突。");
                }
            }
            StoreFiles.DeleteTree(store.Root,relative);
        }
    }
    private static Dictionary<string,ZipArchiveEntry> ValidateArchive(ZipArchive zip)
    {
        if(zip.Entries.Count is 0 or >20000) throw new InvalidDataException("归档文件数超限。");
        var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long total=0;
        foreach(var entry in zip.Entries)
        {
            var path=ProductManifest.Relative(entry.FullName);
            if(!entries.TryAdd(path,entry) || entry.Length is <0 or >2147483648 || ((entry.ExternalAttributes>>16)&0xf000)==0xa000 || (entry.ExternalAttributes&0x400)!=0) throw new InvalidDataException("归档包含重复路径、链接或超大文件。");
            total+=entry.Length; if(total>20L*1024*1024*1024) throw new InvalidDataException("归档总大小超限。");
        }
        foreach(var path in entries.Keys) for(var parent=Path.GetDirectoryName(path)?.Replace('\\','/');!string.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent)?.Replace('\\','/')) if(entries.ContainsKey(parent)) throw new InvalidDataException("归档文件与目录冲突。");
        return entries;
    }
    private static async Task CopyBounded(Stream input,Stream output,long length,CancellationToken cancellation)
    {
        using(input)
        {
            var buffer=new byte[81920]; long count=0;
            for(int read;(read=await input.ReadAsync(buffer,cancellation))!=0;) {count+=read;if(count>length) throw new InvalidDataException("归档实际大小超限。");await output.WriteAsync(buffer.AsMemory(0,read),cancellation);}
            if(count!=length) throw new InvalidDataException("归档不完整。");
        }
    }
}
