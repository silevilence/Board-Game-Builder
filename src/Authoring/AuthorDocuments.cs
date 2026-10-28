using System.Reflection;

namespace BoardGame.Authoring;

public sealed record AuthorDocument(string Topic, string Plugin, string Title, string Markdown);
public static class AuthorDocuments
{
    public static IReadOnlyList<AuthorDocument> Installed(IReadOnlyList<AuthorPlugin> plugins)
    {
        var result = plugins.SelectMany(p => p.Documents).ToList();
        if(result.GroupBy(d=>d.Topic).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate documentation topic");
        return result;
    }
    public static AuthorDocument Bundled(string topic,string plugin,string title)=>new(topic,plugin,title,Read(topic));
    private static string Read(string topic)
    {
        using var stream=typeof(AuthorDocuments).Assembly.GetManifestResourceStream("Authoring.docs."+topic+".md")
            ?? throw new FileNotFoundException("Missing bundled author document: "+topic);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static string Version => typeof(AuthorDocuments).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}

