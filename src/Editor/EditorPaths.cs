namespace BoardGame.Editor;
internal static class EditorPaths
{
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
