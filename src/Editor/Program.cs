using BoardGame.View;
namespace BoardGame.Editor;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.FirstOrDefault()=="--smoke") return ViewportApp.Run("Editor viewport probe",args);
        if(args.Length==2 && args[0]=="--verify-foundation") return ViewportApp.Run("",[],()=>FoundationVerification.Create(args[1]));
        if(args.Length==4 && args[0]=="--verify") return ViewportApp.Run("",[],()=> {
            var window=new EditorWindow(args[1]); window.Opened+=async (_,_)=>await window.VerifyWorkflow(args[2],args[3]); return window;
        });
        return ViewportApp.Run("",args,()=>new EditorHost(args.FirstOrDefault(a=>!a.StartsWith("--")),args.Contains("--base-only")).Window);
    }
}
