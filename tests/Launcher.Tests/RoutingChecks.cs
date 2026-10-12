using BoardGame.Launcher;
using BoardGame.Authoring;

internal static class RoutingChecks
{
    public static void Run(string root,InstallationRegistry registry)
    {
        Directory.CreateDirectory(root);var routing=new ToolRouting(registry);var binding=new ProjectVersions(registry);binding.Bind(root,"0.2.0");
        var route=routing.Prepare(null,root,["describe","project.validate"]);
        Assert(route.Selection.Version=="0.2.0"&&route.Arguments.SequenceEqual(new[]{"describe","project.validate","--project",root}),"bound CLI project injection");
        var json="{\"text\":\"spaces 中文 'quoted'\"}";
        route=routing.Prepare("0.2.1",root,["call","files.write","--args",json]);Assert(route.Arguments[3]==json&&route.Selection.Version=="0.2.1","arguments remain exact");
        try {routing.Prepare(null,root,["profile","--project",root+"-other"]);throw new Exception("conflict accepted");}catch(ArgumentException) { }
        File.WriteAllText(Path.Combine(root,"author.profile.json"),"{\"defaultProfile\":\"gui\",\"profiles\":{\"gui\":{\"format\":1,\"plugins\":[{\"id\":\"edit.gui\"}]}}}",new System.Text.UTF8Encoding(true));
        try {routing.Prepare(null,root,["profile"]);throw new Exception("GUI fell back to disk");}catch(InvalidOperationException) { }
        var cli=Path.Combine(root,"launcher.exe");File.WriteAllText(cli,"fixture");var docs=Path.Combine(root,"docs");Directory.CreateDirectory(docs);File.WriteAllText(Path.Combine(docs,"entry.md"),"docs");
        var template="---\nname: bgb-author\ndescription: Test\n---\n& {{CLI_PS}} docs\n{{DOCS_LINK}}\n";
        var target=Path.Combine(root,"skills");var oldName="bgb-author-v0-2-0";var newName="bgb-author-v0-2-1";
        var old=SkillInstaller.Install(target,cli,docs,template,name:oldName,version:"0.2.0",arguments:["cli","--version","0.2.0","--"]);
        var newer=SkillInstaller.Install(target,cli,docs,template,name:newName,version:"0.2.1",arguments:["cli","--version","0.2.1","--"]);
        try
        {
            var text=File.ReadAllText(Path.Combine(old.Target,"SKILL.md"));Assert(text.Contains("'--version' '0.2.0' '--'")&&text.Contains("name: "+oldName),"pinned command and distinct identity");
            File.Delete(Path.Combine(old.Target,"SKILL.md"));var repaired=SkillInstaller.Install(target,cli,docs,template,repair:true,name:oldName,version:"0.2.0",arguments:["cli","--version","0.2.0","--"]);
            Assert(repaired.Healthy&&SkillInstaller.Status(target,newName).Healthy,"independent repair");
            var moved=Path.Combine(root,"new-launcher.exe");File.WriteAllText(moved,"fixture");
            Assert(SkillInstaller.Install(target,moved,docs,template,repair:true,name:oldName,version:"0.2.0",arguments:["cli","--version","0.2.0","--"]).Healthy,"manual launcher update keeps pinned route");
            SkillInstaller.Uninstall(target,oldName);Assert(SkillInstaller.Status(target,newName).Healthy,"independent uninstall");
        }
        finally {if(SkillInstaller.Status(target,oldName).Installed) SkillInstaller.Uninstall(target,oldName);SkillInstaller.Uninstall(target,newName);}
        Console.WriteLine("Launcher routing/pinned skill coexistence checks passed.");
    }
    private static void Assert(bool condition,string name) {if(!condition) throw new Exception(name);}
}
