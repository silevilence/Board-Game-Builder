using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record RemovalResult(string Version,bool DirectoryRemoved,bool ChooseDefault,int CollectedObjects);

public sealed class InstallationMaintenance(InstallationRegistry registry)
{
    private sealed record RemovalJournal(InstalledVersion Installation,byte[] Manifest);
    public RunningTool[] Running(string version)
    {
        version=ProductManifest.NormalizeVersion(version);using var gate=registry.Lock();var settings=registry.Read();
        var item=settings.Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("版本未登记。");
        return ProcessUse.Find(item.Directory,ManifestCache.Read(registry,item),settings.Installations.Where(i=>i.Version!=version).Select(i=>i.Directory));
    }
    public async Task<RemovalResult> Uninstall(string version,CancellationToken cancellation=default)
    {
        version=ProductManifest.NormalizeVersion(version);var store=new ContentStore(registry.Read().StorageRoot);
        using var operation=await store.Begin(cancellation);InstallationRepair.RecoverLocked(registry,store); RecoverLocked(registry,store);InstallationService.RecoverLocked(registry,store);
        bool chooseDefault,removedDirectory;
        using(var gate=registry.Lock())
        {
            var settings=registry.Read();var item=settings.Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("版本未登记。");
            var manifest=ManifestCache.Read(registry,item);
            var other=settings.Installations.Where(i=>i.Version!=version).Select(i=>i.Directory).ToArray();
            RequireStopped(item.Directory,manifest,other);
            chooseDefault=settings.DefaultVersion==version;removedDirectory=item.Managed;
            if(item.Managed)
            {
                var expected=ProductManifest.SafePath(store.Root,"versions/"+version);
                if(!item.Directory.Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("托管目录不属于当前存储根目录。");
                if(Directory.Exists(item.Directory)) RequireOnlyOwnedFiles(store,item.Directory,manifest);
                var removal=ProductManifest.SafePath(store.Root,"removing/"+version);var journal=ProductManifest.SafePath(store.Root,"removing/"+version+".json");
                Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
                var temporary=journal+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,new RemovalJournal(item,manifest.Bytes));file.Flush(true);}File.Move(temporary,journal);}
                finally {if(File.Exists(temporary)) File.Delete(temporary);}
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    // Taking the original name away prevents a new direct entry from opening it during deletion.
                    if(Directory.Exists(item.Directory))
                    {
                        Directory.Move(item.Directory,removal);
                        RequireStopped(removal,manifest,other,item.Directory);
                        StoreFiles.DeleteTree(store.Root,"removing/"+version);
                    }
                }
                catch
                {
                    if(Directory.Exists(removal) && !Directory.Exists(item.Directory))
                    {
                        Directory.Move(removal,item.Directory);
                        if(!File.Exists(Path.Combine(item.Directory,"toolkit.json"))) File.WriteAllBytes(Path.Combine(item.Directory,"toolkit.json"),manifest.Bytes);
                    }
                    throw new IOException("卸载未完成；目录与登记已保留，部分删除时请修复该版本后重试。");
                }
                registry.Write(Remove(settings,version));File.Delete(journal);
            }
            else registry.Write(Remove(settings,version));
        }
        var collected=CollectLocked(registry,store);operation.Commit();return new(version,removedDirectory,chooseDefault,collected);
    }
    public async Task<int> Collect(CancellationToken cancellation=default)
    {
        var store=new ContentStore(registry.Read().StorageRoot);using var operation=await store.Begin(cancellation);
        InstallationRepair.RecoverLocked(registry,store);RecoverLocked(registry,store);InstallationService.RecoverLocked(registry,store);var result=CollectLocked(registry,store);operation.Commit();return result;
    }
    internal static void RecoverLocked(InstallationRegistry registry,ContentStore store)
    {
        var root=ProductManifest.SafePath(store.Root,"removing");if(!Directory.Exists(root)) return;
        using var gate=registry.Lock();
        foreach(var temporary in Directory.EnumerateFiles(root,"*.tmp"))
            if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(temporary),@"\A[0-9]+\.[0-9]+\.[0-9]+\.json\.[0-9a-f]{32}\.tmp\z")) File.Delete(ProductManifest.SafePath(root,Path.GetFileName(temporary)));
        foreach(var journal in Directory.EnumerateFiles(root,"*.json"))
        {
            ProductManifest.SafePath(root,Path.GetFileName(journal));
            if(new FileInfo(journal).Length>24*1024*1024) throw new InvalidDataException("卸载记录过大。");
            var record=JsonSerializer.Deserialize<RemovalJournal>(File.ReadAllBytes(journal))??throw new InvalidDataException("卸载记录损坏。");
            var item=record.Installation;var manifest=ProductManifest.Parse(record.Manifest);
            if(!item.Managed || manifest.Digest!=item.ManifestHash || manifest.Version!=item.Version || Path.GetFileName(journal)!=item.Version+".json") throw new InvalidDataException("卸载记录身份冲突。");
            var original=ProductManifest.SafePath(store.Root,"versions/"+item.Version);var removal=ProductManifest.SafePath(store.Root,"removing/"+item.Version);
            if(!original.Equals(item.Directory,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("卸载记录位置冲突。");
            var settings=registry.Read();var registered=settings.Installations.SingleOrDefault(i=>i.Version==item.Version);
            if(registered!=null && registered!=item) throw new InvalidDataException("卸载恢复与当前登记冲突。");
            if(registered!=null && Directory.Exists(original) && !Directory.Exists(removal)) {File.Delete(journal);continue;}
            RequireStopped(Directory.Exists(removal)?removal:original,manifest,settings.Installations.Where(i=>i.Version!=item.Version).Select(i=>i.Directory),original);
            if(Directory.Exists(removal))
            {
                if(Directory.Exists(original)) throw new InvalidDataException("卸载恢复时原位置已存在，已保留两个目录。");
                if(registered!=null)
                {
                    Directory.Move(removal,original);
                    if(!File.Exists(Path.Combine(original,"toolkit.json"))) File.WriteAllBytes(Path.Combine(original,"toolkit.json"),manifest.Bytes);
                }
                else StoreFiles.DeleteTree(store.Root,"removing/"+item.Version);
            }
            else if(!Directory.Exists(original) && registered!=null) registry.Write(Remove(settings,item.Version));
            File.Delete(journal);
        }
        if(Directory.EnumerateDirectories(root).Any()) throw new InvalidDataException("存在没有卸载记录的保留目录，停止回收。");
    }
    internal static void RequireStopped(string directory,ProductManifest manifest,IEnumerable<string> others,string? original=null)
    {
        var running=ProcessUse.Find(directory,manifest,others,original);
        if(running.Length!=0) throw new InvalidOperationException("版本仍在使用中，请先关闭 Editor、CLI、试玩或工作进程："+string.Join(", ",running.Select(p=>p.ProcessId)));
    }
    private static LauncherSettings Remove(LauncherSettings settings,string version)=>settings with {Installations=settings.Installations.Where(i=>i.Version!=version).ToArray(),DefaultVersion=settings.DefaultVersion==version?null:settings.DefaultVersion};
    private static int CollectLocked(InstallationRegistry registry,ContentStore store)
    {
        using var gate=registry.Lock();var referenced=new HashSet<string>();
        var settings=registry.Read();var roots=settings.Installations.Select(i=>i.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var versions=ProductManifest.SafePath(store.Root,"versions");if(Directory.Exists(versions)) roots.UnionWith(Directory.EnumerateDirectories(versions));
        foreach(var root in roots)
        {
            // Unknown or missing manifests stop collection rather than guessing which content is unused.
            var record=settings.Installations.FirstOrDefault(i=>i.Directory.Equals(root,StringComparison.OrdinalIgnoreCase));
            var manifest=record!=null?ManifestCache.Read(registry,record):ProductManifest.Read(ProductManifest.SafePath(root,"toolkit.json"));
            referenced.UnionWith(manifest.Files.Select(f=>f.Sha256));
        }
        var count=0;
        foreach(var path in Directory.EnumerateFiles(ProductManifest.SafePath(store.Root,"objects")))
        {
            var hash=Path.GetFileName(path);if(!ProductManifest.IsHash(hash)||referenced.Contains(hash)) continue;
            StoreFiles.DeleteFile(store.ObjectPath(hash));count++;
        }
        return count;
    }
    private static void RequireOnlyOwnedFiles(ContentStore store,string directory,ProductManifest manifest)
    {
        var owned=(manifest.Legacy?LegacyOwnership.Read(store,manifest).Files:manifest.Files.Select(f=>f.Path).Append("toolkit.json")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ExportCacheOwnership.Add(directory,manifest,owned);
        owned.Add(".state/layout.lock");var parents=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in owned) for(var parent=Path.GetDirectoryName(file)?.Replace('\\','/');!string.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent)?.Replace('\\','/')) parents.Add(parent);
        Visit(directory);
        void Visit(string current)
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(current))
            {
                var relative=Path.GetRelativePath(directory,path).Replace('\\','/');ProductManifest.SafePath(directory,relative);
                if(Directory.Exists(path)) {if(!parents.Contains(relative)) throw new IOException("版本目录中有用户目录，已保留，请先移出："+relative);Visit(path);}
                else if(!owned.Contains(relative)) throw new IOException("版本目录中有未登记文件，已保留，请先移出："+relative);
            }
        }
    }
}
