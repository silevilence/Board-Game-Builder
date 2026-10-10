namespace BoardGame.Editor;
internal static class EditorPaths
{
    public static string DebugPlayer => ProductPath("debug-player/Player.exe", "src/Player/bin/Release/net10.0-windows/Player.exe");
    public static string ReleasePlayer => ProductPath("player", "artifacts/player-release-m25");
    private static string ProductPath(string relative,string development)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
        return File.Exists(Path.Combine(root,"toolkit.json"))
            ? Path.GetFullPath(Path.Combine(root,relative)) : InWorkspace(development);
    }
    public static string Workspace
    {
        get
        {
            for(var path=new DirectoryInfo(AppContext.BaseDirectory);path!=null;path=path.Parent)
                if(File.Exists(Path.Combine(path.FullName,"BoardGameBuilder.slnx"))) return path.FullName;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Board Game Builder");
        }
    }
    public static string InWorkspace(string path)=>Path.GetFullPath(Path.Combine(Workspace,path));
}
