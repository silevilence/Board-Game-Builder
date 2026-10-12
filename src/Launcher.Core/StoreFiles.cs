using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BoardGame.Launcher;

internal static class StoreFiles
{
    internal static void DeleteTree(string root,string relative)
    {
        var target=ProductManifest.SafePath(root,relative);
        if(!Directory.Exists(target)) return;
        // Validate each level before traversing it; never follow a junction during recovery.
        foreach(var entry in Directory.EnumerateFileSystemEntries(target))
        {
            var child=Path.GetRelativePath(root,entry).Replace('\\','/'); ProductManifest.SafePath(root,child);
            if(Directory.Exists(entry)) DeleteTree(root,child); else DeleteFile(entry);
        }
        Directory.Delete(target);
    }
    internal static void DeleteFile(string path)
    {
        // Remove only this NTFS link, including readonly links, without changing attributes on other installations.
        using var handle=CreateFile(@"\\?\"+Path.GetFullPath(path),0x10000,7,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        uint flags=1|2|16;
        if(handle.IsInvalid || !SetFileInformationByHandle(handle,21,ref flags,4)) throw new IOException("文件仍被占用或无法删除："+path,new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref uint information,uint size);
}
