namespace BoardGame.Authoring;

public sealed record AuthorArtifact(ProjectSource Source,string Target,string Kind,string Fingerprint)
{public IReadOnlyList<string> Warnings {get;init;}=[];}
public static class ExportOperations
{
    // Explicit legacy profile bundle, not a fallback when either provider is missing.
    public static AuthorPlugin Plugin=>new("author.export","1",1,[],[],_=>{},"Legacy export bundle: package and standalone Player")
    {
        Includes=[new("author.export.package"),new("author.export.player")],
        Documents=[AuthorDocuments.Bundled("export","author.export","打包和 Player 导出")]
    };
}
