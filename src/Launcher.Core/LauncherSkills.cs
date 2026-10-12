using BoardGame.Authoring;

namespace BoardGame.Launcher;

public sealed class LauncherSkills(InstallationRegistry registry,string executable)
{
    public SkillInstallStatus Execute(string action,string version,string target,string? method=null)
    {
        version=ProductManifest.NormalizeVersion(version);var name="bgb-author-v"+version.Replace('.','-');
        if(action=="uninstall") return SkillInstaller.Uninstall(target,name);
        if(action=="status") return SkillInstaller.Status(target,name);
        if(action is not ("install" or "repair")) throw new ArgumentException("skill install|repair|status|uninstall");
        using var gate=registry.Lock();var installation=registry.Read().Installations.SingleOrDefault(i=>i.Version==version)??throw new InvalidOperationException("指定技能版本未安装。");
        var status=registry.Inspect(installation);if(status.State!="available") throw new InvalidOperationException(status.Diagnostic);
        var manifest=ProductManifest.Read(ProductManifest.SafePath(installation.Directory,"toolkit.json"));
        var docs=Path.GetDirectoryName(ProductManifest.SafePath(installation.Directory,manifest.Json.GetProperty("entries").GetProperty("documentation").GetString()!))!;
        var template=ProductManifest.SafePath(installation.Directory,manifest.Legacy?"templates/bgb-author/SKILL.md.template":".payload/templates/bgb-author/SKILL.md.template");
        var arguments=new[]{"--home",registry.Home,"cli","--version",version,"--"};
        var text=File.ReadAllText(template)+"\n创作版本已固定为 V"+version+"。后续调用保留上述显式版本；安装新版或改变默认不会改变此技能。GUI 连接使用 workspaces 查询同版实例，并保留 --instance 与 --project；版本不匹配时先纠正选择，不切换工具或磁盘模式。\n";
        return SkillInstaller.Install(target,executable,docs,text,method??SkillInstaller.Status(target,name).Installation?.Method??"copy",action=="repair",name,version,arguments);
    }
}
