using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace BoardGame.Launcher;

public sealed record TransferProgress(string Stage,string? Component,long DownloadedBytes,long TotalBytes);
public sealed record ContentPlan(int ReusedObjects,int MissingObjects,long MissingBytes,ProductComponent[] Downloads);

/// <summary>A single cross-process store transaction protects objects from concurrent installation and collection.</summary>
public sealed class ContentStore(string root)
{
    public string Root {get;}=Path.GetFullPath(root);
    public string ObjectPath(string hash)=>ProductManifest.IsHash(hash)?ProductManifest.SafePath(Root,"objects/"+hash):throw new InvalidDataException("无效内容身份。");
    public async Task<StoreOperation> Begin(CancellationToken cancellation=default)
    {
        CheckLocation(); Directory.CreateDirectory(ProductManifest.SafePath(Root,"objects"));
        var lockPath=ProductManifest.SafePath(Root,"operation.lock");
        FileStream gate;
        for(;;)
        {
            cancellation.ThrowIfCancellationRequested();
            try {gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);break;}
            catch(IOException) {await Task.Delay(100,cancellation);}
        }
        try
        {
            foreach(var path in Directory.EnumerateFiles(Root,"transfer-*.tmp"))
                if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),@"\Atransfer-[0-9a-f]{32}\.tmp\z")) File.Delete(ProductManifest.SafePath(Root,Path.GetFileName(path)));
            foreach(var path in Directory.EnumerateFiles(ProductManifest.SafePath(Root,"objects"),"*.tmp"))
                if(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),@"\A[0-9a-f]{64}\.[0-9a-f]{32}\.tmp\z")) File.Delete(ProductManifest.SafePath(Root,"objects/"+Path.GetFileName(path)));
            return new(this,gate);
        }
        catch {gate.Dispose();throw;}
    }
    public ContentPlan Plan(ProductManifest manifest)
    {
        if(manifest.Legacy) throw new InvalidOperationException("旧包使用完整归档导入。");
        var files=manifest.Files.DistinctBy(f=>f.Sha256).ToArray();
        var missing=files.Where(f=>!Valid(f)).ToArray(); var hashes=missing.Select(f=>f.Sha256).ToHashSet();
        return new(files.Length-missing.Length,missing.Length,missing.Sum(f=>f.Size),manifest.Components.Where(c=>c.Objects.Any(hashes.Contains)).ToArray());
    }
    internal bool Valid(ProductFile file)
    {
        var path=ObjectPath(file.Sha256);
        return File.Exists(path)&&new FileInfo(path).Length==file.Size&&ProductManifest.HashFile(path)==file.Sha256;
    }
    public void CheckLocation()
    {
        if(!OperatingSystem.IsWindows() || Root.StartsWith(@"\\",StringComparison.Ordinal)) throw new IOException("共享存储需要本机 Windows NTFS 磁盘。");
        ProductManifest.SafePath(Root,"objects"); Directory.CreateDirectory(Root);
        var drive=new DriveInfo(Path.GetPathRoot(Root)!);
        if(drive.DriveType!=DriveType.Fixed || drive.DriveFormat!="NTFS") throw new IOException("共享存储需要本机 NTFS 固定磁盘，版本目录必须位于同一存储根目录。");
        var probe=ProductManifest.SafePath(Root,"probe-"+Guid.NewGuid().ToString("N")); var link=probe+".link";
        try {File.WriteAllBytes(probe,[1]); Link(link,probe);}
        finally {if(File.Exists(link)) File.Delete(link); if(File.Exists(probe)) File.Delete(probe);}
    }
    internal void CheckSpace(long bytes)
    {
        if(bytes<0 || bytes>new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace-16*1024*1024) throw new IOException("磁盘空间不足，无法安全完成安装。");
    }
    public static void Link(string destination,string source)
    {
        if(!CreateHardLink(@"\\?\"+Path.GetFullPath(destination),@"\\?\"+Path.GetFullPath(source),IntPtr.Zero)) throw new IOException("无法建立同卷内容硬链接。",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string link,string existing,IntPtr security);
}

public sealed class StoreOperation:IDisposable
{
    private readonly ContentStore store;
    private readonly FileStream gate;
    private readonly List<string> added=[];
    private bool committed,disposed;
    public long DownloadedBytes {get;private set;}
    public List<string> RequestedComponents {get;}=[];
    internal StoreOperation(ContentStore store,FileStream gate) {this.store=store;this.gate=gate;}
    public void Commit() {ObjectDisposedException.ThrowIf(disposed,this);committed=true;}
    public async Task Fetch(ProductManifest manifest,HttpClient client,IProgress<TransferProgress>? progress=null,CancellationToken cancellation=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        var plan=store.Plan(manifest); store.CheckSpace(plan.MissingBytes+plan.Downloads.Select(c=>c.Size).DefaultIfEmpty().Max());
        var files=manifest.Files.DistinctBy(f=>f.Sha256).ToDictionary(f=>f.Sha256);
        foreach(var component in plan.Downloads)
        {
            cancellation.ThrowIfCancellationRequested();
            if(component.Source.Repository!=ReleaseCatalog.Repository) throw new InvalidDataException("组件来源不是官方仓库。");
            var archive=ProductManifest.SafePath(store.Root,"transfer-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                var url=$"https://github.com/{component.Source.Repository}/releases/download/{component.Source.Tag}/{component.Source.Asset}";
                ReleaseCatalog.ValidateUrl(url); using var request=ReleaseCatalog.Request(url);
                using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is {} length && length!=component.Size) throw new InvalidDataException("组件附件大小不符。");
                RequestedComponents.Add(component.Id);
                await using(var input=await response.Content.ReadAsStreamAsync(cancellation))
                await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                {
                    var buffer=new byte[81920]; long received=0;
                    for(int count;(count=await input.ReadAsync(buffer,cancellation))!=0;)
                    {
                        received+=count; DownloadedBytes+=count;
                        if(received>component.Size) throw new InvalidDataException("组件下载超过声明大小。");
                        await output.WriteAsync(buffer.AsMemory(0,count),cancellation);
                        progress?.Report(new("download",component.Id,DownloadedBytes,plan.Downloads.Sum(c=>c.Size)));
                    }
                    await output.FlushAsync(cancellation);
                }
                ProductManifest.VerifyFile(archive,component.Size,component.Sha256);
                using var zip=ZipFile.OpenRead(archive);
                if(zip.Entries.Count!=component.Objects.Length || !zip.Entries.Select(e=>e.FullName).Order(StringComparer.Ordinal).SequenceEqual(component.Objects.Select(h=>"objects/"+h).Order(StringComparer.Ordinal))) throw new InvalidDataException("组件归档包含未知、重复或缺少的对象。");
                foreach(var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested(); var file=files[entry.FullName[8..]];
                    if(entry.Length!=file.Size || ((entry.ExternalAttributes>>16)&0xf000)==0xa000 || (entry.ExternalAttributes&0x400)!=0) throw new InvalidDataException("组件包含链接或大小不符。");
                    await using var input=entry.Open(); await Add(file,input,cancellation);
                }
            }
            finally {if(File.Exists(archive)) File.Delete(archive);}
        }
    }
    public async Task Add(ProductFile file,Stream input,CancellationToken cancellation=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this); cancellation.ThrowIfCancellationRequested();
        var destination=store.ObjectPath(file.Sha256);
        if(store.Valid(file)) return;
        // Replacing a linked corrupt object would leave installed aliases stale. Repair is an explicit installation operation.
        if(File.Exists(destination)) throw new InvalidDataException("共享内容损坏，请修复其引用版本："+file.Sha256);
        store.CheckSpace(file.Size); var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920]; long count=0; using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for(int read;(read=await input.ReadAsync(buffer,cancellation))!=0;) {count+=read; if(count>file.Size) throw new InvalidDataException("对象大于声明大小。"); hash.AppendData(buffer,0,read); await output.WriteAsync(buffer.AsMemory(0,read),cancellation);}
                if(count!=file.Size || Convert.ToHexStringLower(hash.GetHashAndReset())!=file.Sha256) throw new InvalidDataException("对象内容校验失败。");
                await output.FlushAsync(cancellation); output.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested(); File.Move(temporary,destination); File.SetAttributes(destination,FileAttributes.ReadOnly); added.Add(destination);
        }
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        try {if(!committed) foreach(var path in added) {File.SetAttributes(path,FileAttributes.Normal); File.Delete(path);}}
        finally {gate.Dispose();}
    }
}
