namespace BoardGame.Runtime;

// Namespace retained for existing release-manifest consumers.
public sealed record ReleaseManifest(int Api,string Version,string Platform,SortedDictionary<string,string> Files,bool GuiEntry=false,bool EditorDebug=false,
    SortedDictionary<string,string>? RuntimePlugins=null,int RuntimeApi=1,string EntryPoint="Player.exe",int PackageFormat=2);
