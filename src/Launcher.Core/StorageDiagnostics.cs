using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BoardGame.Launcher;

public sealed record StorageMeasurements(int Versions,int PhysicalObjects,long PhysicalBytes,long AllocatedBytes,long LogicalBytes,int LogicalFiles,int CoreRuntimeFiles,int DesktopRuntimeFiles);

public static class StorageDiagnostics
{
    internal static bool SameFile(string first,string second)=>Read(first).Identity==Read(second).Identity;
    /// <summary>Measure NTFS identities and allocation, rather than summing apparent hard-link sizes.</summary>
    public static StorageMeasurements Measure(ContentStore store)
    {
        var objects=new Dictionary<string,string>(); var identities=new HashSet<string>(); long bytes=0,allocated=0,logical=0; var logicalFiles=0; var versions=0;
        var cores=new HashSet<string>(); var desktops=new HashSet<string>();
        foreach(var path in Directory.EnumerateFiles(ProductManifest.SafePath(store.Root,"objects")))
        {
            var hash=Path.GetFileName(path); if(!ProductManifest.IsHash(hash)) continue;
            ProductManifest.SafePath(store.Root,"objects/"+hash);
            if(ProductManifest.HashFile(path)!=hash) throw new InvalidDataException("共享对象损坏："+hash);
            var info=Read(path); if(!identities.Add(info.Identity)) throw new InvalidDataException("不同内容身份指向同一物理文件。");
            objects.Add(hash,info.Identity); bytes+=new FileInfo(path).Length; allocated+=info.Allocated;
        }
        var versionRoot=ProductManifest.SafePath(store.Root,"versions");
        if(Directory.Exists(versionRoot)) foreach(var root in Directory.EnumerateDirectories(versionRoot))
        {
            var manifest=ProductManifest.Read(ProductManifest.SafePath(root,"toolkit.json")); if(manifest.Legacy) continue; versions++;
            foreach(var file in manifest.Files)
            {
                var info=Read(ProductManifest.SafePath(root,file.Path));
                if(!objects.TryGetValue(file.Sha256,out var expected) || info.Identity!=expected) throw new InvalidDataException("版本文件没有共享相同内容："+file.Path);
                logical+=file.Size; logicalFiles++;
                if(Path.GetFileName(file.Path)=="coreclr.dll") cores.Add(info.Identity);
                if(Path.GetFileName(file.Path)=="System.Windows.Forms.dll") desktops.Add(info.Identity);
            }
        }
        return new(versions,objects.Count,bytes,allocated,logical,logicalFiles,cores.Count,desktops.Count);
    }
    private static (string Identity,long Allocated) Read(string path)
    {
        using var file=File.OpenRead(path);
        if(!GetFileInformationByHandle(file.SafeFileHandle,out var id) || !GetFileInformationByHandleEx(file.SafeFileHandle,1,out var size,24)) throw new IOException("无法读取物理文件身份。",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        return ($"{id.Volume}:{id.IndexHigh}:{id.IndexLow}",size.Allocation);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Identity {public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo {public long Allocation,Length; public uint Links; public byte DeletePending,Directory;}
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Identity identity);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle,int kind,out StandardInfo info,uint size);
}
