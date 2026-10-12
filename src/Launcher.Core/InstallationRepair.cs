using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoardGame.Launcher;

public sealed record RepairResult(string[] Versions,int ReplacedObjects,long DownloadedBytes);

public sealed class InstallationRepair(InstallationRegistry registry,HttpClient client)
{
    private sealed record RepairJournal(InstalledVersion[] Installations);
    public async Task<RepairResult> Repair(string version,IProgress<TransferProgress>? progress=null,CancellationToken cancellation=default)
    {
        version=ProductManifest.NormalizeVersion(version);var store=new ContentStore(registry.Read().StorageRoot);
        using var operation=await store.Begin(cancellation);RecoverLocked(registry,store);InstallationMaintenance.RecoverLocked(registry,store);InstallationService.RecoverLocked(registry,store);
        var settings=registry.Read();var target=settings.Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("版本未登记。");
        if(!target.Managed || target.Mode=="legacy") throw new InvalidOperationException("仅新格式托管安装支持共享修复；外部／旧包请保留工程后重新解压原始包。");
        var manifest=ManifestCache.Read(registry,target);_=RuntimePolicy.Resolve(manifest);
        var bad=manifest.Files.DistinctBy(f=>f.Sha256).Where(f=>!store.Valid(f)).Select(f=>f.Sha256).ToHashSet();
        var affected=settings.Installations.Where(i=>i.Managed && i.Mode!="legacy").Select(i=>(Installation:i,Manifest:ManifestCache.Read(registry,i))).Where(p=>p.Installation.Version==version || p.Manifest.Files.Any(f=>bad.Contains(f.Sha256))).ToArray();
        foreach(var pair in affected)
        {
            RequireManagedPath(store,pair.Installation);
            InstallationMaintenance.RequireStopped(pair.Installation.Directory,pair.Manifest,settings.Installations.Where(i=>i.Version!=pair.Installation.Version).Select(i=>i.Directory));
        }
        var relative="repairs/"+Guid.NewGuid().ToString("N");var staging=new ContentStore(ProductManifest.SafePath(store.Root,relative+"/content"));
        var repairRoot=ProductManifest.SafePath(store.Root,relative);Directory.CreateDirectory(repairRoot);
        long downloaded=0;var mutationStarted=false;
        try
        {
            using(var stagedOperation=await staging.Begin(cancellation))
            {
                foreach(var file in manifest.Files.DistinctBy(f=>f.Sha256))
                {
                    cancellation.ThrowIfCancellationRequested();if(!bad.Contains(file.Sha256)) ContentStore.Link(staging.ObjectPath(file.Sha256),store.ObjectPath(file.Sha256));
                }
                await stagedOperation.Fetch(manifest,client,progress,cancellation);downloaded=stagedOperation.DownloadedBytes;stagedOperation.Commit();
            }
            using(var gate=registry.Lock())
            {
                settings=registry.Read();
                foreach(var pair in affected)
                {
                    if(!settings.Installations.Contains(pair.Installation)) throw new InvalidOperationException("安装登记在修复期间改变，请重试。");
                    InstallationMaintenance.RequireStopped(pair.Installation.Directory,pair.Manifest,settings.Installations.Where(i=>i.Version!=pair.Installation.Version).Select(i=>i.Directory));
                    foreach(var file in pair.Manifest.Files) if(Directory.Exists(ProductManifest.SafePath(pair.Installation.Directory,file.Path))) throw new IOException("产品文件位置被目录占用，请保留并移出："+file.Path);
                }
                cancellation.ThrowIfCancellationRequested();
                var journal=ProductManifest.SafePath(repairRoot,"journal.json");
                var temporary=ProductManifest.SafePath(repairRoot,"journal.tmp");
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(output,new RepairJournal(affected.Select(p=>p.Installation).ToArray()));output.Flush(true);}
                File.Move(temporary,journal);
                mutationStarted=true;
                foreach(var pair in affected)
                {
                    var backup=ProductManifest.SafePath(repairRoot,"backups/"+pair.Installation.Version);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    if(Directory.Exists(pair.Installation.Directory)) Directory.Move(pair.Installation.Directory,backup); else Directory.CreateDirectory(backup);
                    InstallationMaintenance.RequireStopped(backup,pair.Manifest,settings.Installations.Where(i=>i.Version!=pair.Installation.Version).Select(i=>i.Directory),pair.Installation.Directory);
                }
                // Every replacement is a newly verified file. Existing mapped/hard-linked content is never rewritten in place.
                foreach(var hash in bad)
                {
                    var destination=store.ObjectPath(hash);if(File.Exists(destination)) StoreFiles.DeleteFile(destination);
                    File.Move(staging.ObjectPath(hash),destination);
                }
                foreach(var pair in affected)
                {
                    var backup=ProductManifest.SafePath(repairRoot,"backups/"+pair.Installation.Version);
                    foreach(var file in pair.Manifest.Files)
                    {
                        var path=ProductManifest.SafePath(backup,file.Path);var source=store.ObjectPath(file.Sha256);
                        if(File.Exists(path)&&StorageDiagnostics.SameFile(path,source)) continue;
                        if(File.Exists(path)) StoreFiles.DeleteFile(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);ContentStore.Link(path,source);
                    }
                    var metadata=ProductManifest.SafePath(backup,"toolkit.json");if(File.Exists(metadata)) StoreFiles.DeleteFile(metadata);File.WriteAllBytes(metadata,pair.Manifest.Bytes);
                    pair.Manifest.VerifyDirectory(backup);_=RuntimePolicy.Resolve(pair.Manifest,backup);
                }
                foreach(var pair in affected) Directory.Move(ProductManifest.SafePath(repairRoot,"backups/"+pair.Installation.Version),pair.Installation.Directory);
            }
            StoreFiles.DeleteTree(store.Root,relative);operation.Commit();return new(affected.Select(p=>p.Installation.Version).ToArray(),bad.Count,downloaded);
        }
        catch
        {
            if(mutationStarted) RecoverLocked(registry,store); else StoreFiles.DeleteTree(store.Root,relative);
            throw;
        }
    }
    internal static void RecoverLocked(InstallationRegistry registry,ContentStore store)
    {
        var root=ProductManifest.SafePath(store.Root,"repairs");if(!Directory.Exists(root)) return;
        using var gate=registry.Lock();
        foreach(var directory in Directory.EnumerateDirectories(root))
        {
            var name=Path.GetFileName(directory);if(!Regex.IsMatch(name,@"\A[0-9a-f]{32}\z")) throw new InvalidDataException("未知修复事务目录。");
            var relative="repairs/"+name;var journalPath=ProductManifest.SafePath(store.Root,relative+"/journal.json");
            if(File.Exists(journalPath))
            {
                if(new FileInfo(journalPath).Length>4*1024*1024) throw new InvalidDataException("修复记录过大。");
                var journal=JsonSerializer.Deserialize<RepairJournal>(File.ReadAllBytes(journalPath))??throw new InvalidDataException("修复记录损坏。");
                var settings=registry.Read();
                foreach(var item in journal.Installations)
                {
                    RequireManagedPath(store,item);if(!settings.Installations.Contains(item)) throw new InvalidDataException("修复恢复与安装登记冲突。");
                    var backup=ProductManifest.SafePath(directory,"backups/"+item.Version);
                    if(!Directory.Exists(backup)) continue;
                    if(Directory.Exists(item.Directory)) throw new InvalidDataException("修复恢复时原位置已存在，保留两个目录。");
                    var manifest=ManifestCache.Read(registry,item);InstallationMaintenance.RequireStopped(backup,manifest,settings.Installations.Where(i=>i.Version!=item.Version).Select(i=>i.Directory),item.Directory);
                    Directory.Move(backup,item.Directory);
                    if(!File.Exists(Path.Combine(item.Directory,"toolkit.json"))) File.WriteAllBytes(Path.Combine(item.Directory,"toolkit.json"),manifest.Bytes);
                }
            }
            StoreFiles.DeleteTree(store.Root,relative);
        }
    }
    private static void RequireManagedPath(ContentStore store,InstalledVersion item)
    {
        if(!item.Managed || item.Mode=="legacy" || ProductManifest.NormalizeVersion(item.Version)!=item.Version || !ProductManifest.SafePath(store.Root,"versions/"+item.Version).Equals(item.Directory,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("无效托管版本位置。");
    }
}
