using BoardGame.View;
namespace BoardGame.Editor;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.FirstOrDefault()=="--smoke") return ViewportApp.Run("Editor viewport probe",args);
        if(args.Length==2 && args[0]=="--verify-m3") return ViewportApp.Run("",[],()=>M3Verification.Create(args[1]));
        if(args.Length==2 && args[0]=="--verify-foundation") return ViewportApp.Run("",[],()=>FoundationVerification.Create(args[1]));
        if(args.Length==3 && args[0]=="--verify-session") return ViewportApp.Run("",[],()=>SessionVerification.Create(args[1],args[2]));
        if(args.Length==4 && args[0]=="--verify-export") return ViewportApp.Run("",[],()=>ExportVerification.Create(args[1],args[2],args[3]));
        if(args.Length==3 && args[0]=="--verify-assembly") return ViewportApp.Run("",[],()=>AssemblyVerification.Create(args[1],args[2]));
        return ViewportApp.Run("",args,()=>new EditorHost(args.FirstOrDefault(a=>!a.StartsWith("--")),args.Contains("--base-only")).Window);
    }
}
