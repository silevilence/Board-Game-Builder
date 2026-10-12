using System.Diagnostics;
using System.Text.Json;

namespace BoardGame.Launcher;

public sealed record RoutedTool(VersionSelection Selection,string[] Arguments);
public sealed record AuthorWorkspace(string Instance,string Workspace,long Generation,string? Project,int Process,long ProcessStarted,string? EngineVersion);

public sealed class ToolRouting(InstallationRegistry registry)
{
    public RoutedTool Prepare(string? version,string? project,string[] arguments)
    {
        var command=arguments.FirstOrDefault()??"help";
        var projectCommand=command is not ("help" or "--help" or "docs" or "workspaces" or "skill");
        var options=new Dictionary<string,string>(StringComparer.Ordinal);
        if(projectCommand)
            for(var i=command is "call" or "describe"?2:1;i+1<arguments.Length;i+=2)
                if(!options.TryAdd(arguments[i],arguments[i+1])) throw new ArgumentException("重复 CLI 选项："+arguments[i]);
        var forwardedProject=options.GetValueOrDefault("--project");
        if(project!=null&&forwardedProject!=null&&!string.Equals(ProjectVersions.Locate(project),ProjectVersions.Locate(forwardedProject),StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("路由工程与 CLI 工程不同，拒绝混用。");
        var selection=new ProjectVersions(registry).Resolve(version,project??forwardedProject);selection.RequireAvailable();
        var forwarded=arguments.ToList();
        if(projectCommand&&selection.Project!=null)
        {
            if(forwardedProject==null) {forwarded.Add("--project");forwarded.Add(selection.Project);}
            var instance=options.GetValueOrDefault("--instance");
            var gui=UsesGui(selection.Project,options.GetValueOrDefault("--profile"));
            if(instance!=null&&!gui) throw new InvalidOperationException("--instance 需要选择 edit.gui 作者 profile；不会改用磁盘模式。");
            if(gui)
            {
                var matches=Workspaces().Where(w=>string.Equals(w.Project,selection.Project,StringComparison.OrdinalIgnoreCase)&&(instance==null||w.Instance==instance)).ToArray();
                if(matches.Length!=1) throw new InvalidOperationException("需要唯一的 Editor 工作区连接，请查看 workspaces 并使用 --instance；不会回退磁盘模式。");
                if(matches[0].EngineVersion!=selection.Version) throw new InvalidOperationException("GUI 引擎版本不匹配：CLI V"+selection.Version+"，Editor "+(matches[0].EngineVersion??"身份未知")+"。请显式选择同版 Editor／CLI。");
                if(instance==null) {forwarded.Add("--instance");forwarded.Add(matches[0].Instance);}
            }
        }
        return new(selection,forwarded.ToArray());
    }
    public AuthorWorkspace[] Workspaces()
    {
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","author","connections");
        if(!Directory.Exists(directory)) return [];
        var result=new List<AuthorWorkspace>();var settings=registry.Read();
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            try
            {
                if(new FileInfo(file).Length>65536) continue;
                using var document=JsonDocument.Parse(File.ReadAllText(file));var value=document.RootElement;
                if(value.GetProperty("api").GetInt32()!=1) continue;
                using var process=Process.GetProcessById(value.GetProperty("process").GetInt32());
                if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=value.GetProperty("processStarted").GetInt64()) continue;
                var image=process.MainModule?.FileName;
                var installation=image==null?null:settings.Installations.SingleOrDefault(i=>image.StartsWith(Path.TrimEndingDirectorySeparator(i.Directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                result.Add(new(value.GetProperty("instance").GetString()!,value.GetProperty("workspace").GetString()!,value.GetProperty("generation").GetInt64(),value.GetProperty("project").GetString(),process.Id,process.StartTime.ToUniversalTime().Ticks,installation?.Version));
            }
            catch(Exception e) when(e is IOException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or System.ComponentModel.Win32Exception) { }
        }
        return result.ToArray();
    }
    private static bool UsesGui(string project,string? requested)
    {
        var path=ProductManifest.SafePath(project,"author.profile.json");if(!File.Exists(path)) return false;
        if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("作者 profile 文件过大。");
        using var document=JsonDocument.Parse(File.ReadAllText(path));ProductManifest.Unique(document.RootElement);var root=document.RootElement;
        var name=requested??(root.TryGetProperty("defaultProfile",out var chosen)?chosen.GetString():null);
        if(name==null || !root.TryGetProperty("profiles",out var profiles) || !profiles.TryGetProperty(name,out var profile)) return false;
        return profile.TryGetProperty("plugins",out var plugins)&&plugins.EnumerateArray().Any(p=>p.GetProperty("id").GetString()=="edit.gui");
    }
}
