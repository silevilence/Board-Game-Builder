using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BoardGame.Launcher;

public sealed record RunningTool(int ProcessId,string? Image);

/// <summary>Inspect OS processes and Restart Manager file users, including entries started outside the launcher.</summary>
public static class ProcessUse
{
    public static RunningTool[] Find(string directory,ProductManifest manifest,IEnumerable<string> otherInstallations,string? originalDirectory=null)
    {
        var roots=new[]{directory,originalDirectory}.Where(r=>r!=null).Select(r=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(r!))+Path.DirectorySeparatorChar).ToArray();
        var others=otherInstallations.Select(r=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))+Path.DirectorySeparatorChar).ToArray();
        var processes=new Dictionary<int,string?>();var usingVersion=new HashSet<int>();
        foreach(var process in Process.GetProcesses())
        {
            using(process)
            {
                var image=Image(process.Id);processes[process.Id]=image;
                if(image!=null && roots.Any(root=>image.StartsWith(root,StringComparison.OrdinalIgnoreCase))) usingVersion.Add(process.Id);
                else if(image!=null && Path.GetFileName(image).Equals("dotnet.exe",StringComparison.OrdinalIgnoreCase))
                {
                    var command=CommandLine(process.Id);
                    if(command!=null && roots.Any(root=>command.Contains(root,StringComparison.OrdinalIgnoreCase))) usingVersion.Add(process.Id);
                }
            }
        }
        var executablePaths=manifest.Legacy?LegacyEntries(directory,directory):manifest.Json.GetProperty("programs").EnumerateArray().Select(p=>ProductManifest.SafePath(directory,p.GetProperty("entry").GetString()!)).Concat(ProductManifest.RequiredEntries.Where(e=>e.Key!="documentation").Select(e=>ProductManifest.SafePath(directory,e.Value)));
        var resources=executablePaths.SelectMany(path=>new[]{path,Path.ChangeExtension(path,".dll")}).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var pid in FileUsers(resources))
        {
            var image=processes.GetValueOrDefault(pid)??Image(pid);
            // The same executable bytes may be linked by another installed version. Its own path disambiguates that use.
            if(image==null || !others.Any(root=>image.StartsWith(root,StringComparison.OrdinalIgnoreCase)) || roots.Any(root=>image.StartsWith(root,StringComparison.OrdinalIgnoreCase))) usingVersion.Add(pid);
            processes[pid]=image;
        }
        return usingVersion.Select(pid=>new RunningTool(pid,processes.GetValueOrDefault(pid))).ToArray();
    }
    private static IEnumerable<string> LegacyEntries(string root,string directory)
    {
        if(!Directory.Exists(directory)) yield break;
        foreach(var path in Directory.EnumerateFileSystemEntries(directory))
        {
            ProductManifest.SafePath(root,Path.GetRelativePath(root,path).Replace('\\','/'));
            if(Directory.Exists(path)) {foreach(var file in LegacyEntries(root,path)) yield return file;}
            else if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) yield return path;
        }
    }
    private static string? Image(int pid)
    {
        using var process=OpenProcess(0x1000,false,pid);if(process.IsInvalid) return null;
        var path=new StringBuilder(32768);var length=path.Capacity;
        return QueryFullProcessImageName(process,0,path,ref length)?path.ToString():null;
    }
    private static string? CommandLine(int pid)
    {
        using var process=OpenProcess(0x1000,false,pid);if(process.IsInvalid) return null;
        _=NtQueryInformationProcess(process,60,IntPtr.Zero,0,out var length);if(length<=16 || length>1024*1024) return null;
        var buffer=Marshal.AllocHGlobal(length);
        try
        {
            if(NtQueryInformationProcess(process,60,buffer,length,out _)!=0) return null;
            var count=(ushort)Marshal.ReadInt16(buffer); var text=Marshal.ReadIntPtr(buffer,IntPtr.Size==8?8:4);
            if(text.ToInt64()<buffer.ToInt64() || text.ToInt64()+count>buffer.ToInt64()+length) return null;
            return Marshal.PtrToStringUni(text,count/2);
        }
        finally {Marshal.FreeHGlobal(buffer);}
    }
    private static int[] FileUsers(string[] files)
    {
        if(files.Length==0) return [];
        var result=RmStartSession(out var session,0,new StringBuilder(33));
        if(result!=0) throw new IOException("无法检查工具文件占用。",new System.ComponentModel.Win32Exception(result));
        try
        {
            result=RmRegisterResources(session,(uint)files.Length,files,0,IntPtr.Zero,0,IntPtr.Zero);
            if(result!=0) throw new IOException("无法登记工具占用检查。",new System.ComponentModel.Win32Exception(result));
            uint count=0;result=RmGetList(session,out var needed,ref count,null,out _);
            for(var attempt=0;result==234 && attempt<4;attempt++)
            {
                if(needed>10000) throw new IOException("文件占用进程过多。");
                var entries=new ProcessInfo[needed];count=needed;
                result=RmGetList(session,out needed,ref count,entries,out _);
                if(result==0) return entries.Take((int)count).Select(p=>p.Process.Id).ToArray();
            }
            if(result!=0) throw new IOException("文件占用状态正在变化，请重试。",new System.ComponentModel.Win32Exception(result));
            return [];
        }
        finally {RmEndSession(session);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess {public int Id;public System.Runtime.InteropServices.ComTypes.FILETIME Start;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Application;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Service;
        public uint Type,Status,Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,int id);
    [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process,int flags,StringBuilder name,ref int size);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle process,int kind,IntPtr data,int length,out int needed);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmStartSession(out uint session,uint flags,[Out]StringBuilder key);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmRegisterResources(uint session,uint files,string[] names,uint applications,IntPtr processes,uint services,IntPtr serviceNames);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmGetList(uint session,out uint needed,ref uint count,[In,Out]ProcessInfo[]? applications,out uint reasons);
}
