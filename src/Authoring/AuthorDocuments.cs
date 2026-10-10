using System.Reflection;

namespace BoardGame.Authoring;

public sealed record AuthorDocument(string Topic, string Plugin, string Title, string Markdown);
public static class AuthorDocuments
{
    public static IReadOnlyList<AuthorDocument> Installed(IReadOnlyList<AuthorPlugin> plugins)
    {
        var mapping = new (string Topic, string Plugin, string Title)[]
        {
            ("entry","author.core","工程入口与最小创作"),("profile","author.core","Profile、模式与回执"),
            ("definition-module","author.module","组件定义格式"),("definition-state","author.state","状态定义格式"),
            ("resources","author.resources","图片与图集"),("lua","author.run","Lua 规则和 CPU API"),
            ("debugging","author.run","校验与调试"),("export","author.export","打包和 Player 导出")
        };
        var result = mapping.Where(m => plugins.Any(p => p.Id == m.Plugin)).Select(m => new AuthorDocument(m.Topic,m.Plugin,m.Title,Read(m.Topic))).ToList();
        result.AddRange(plugins.SelectMany(p => p.Documents));
        if(result.GroupBy(d=>d.Topic).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate documentation topic");
        return result;
    }
    private static string Read(string topic)
    {
        using var stream=typeof(AuthorDocuments).Assembly.GetManifestResourceStream("Authoring.docs."+topic+".md")
            ?? throw new FileNotFoundException("Missing bundled author document: "+topic);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static string Version => typeof(AuthorDocuments).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
