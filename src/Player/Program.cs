using BoardGame.View;
namespace BoardGame.Player;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
#if EDITOR_DEBUG
        if(args.FirstOrDefault()=="--editor-debug")
        {
            if(args.Contains("--headless")) { BoardGame.Runtime.DebugTransport.Serve(); return 0; }
            return ViewportApp.Run("",[],()=>new DebugPlayerWindow());
        }
#endif
        if(args.Length==0 || args[0]=="gui")
        {
            var module=args.Length>1?args[1]:null;
            var smoke=args.Length==4 && args[2]=="--smoke"?args[3]:null;
            return ViewportApp.Run("",[],()=>new PlayerWindow(module,smoke));
        }
        if(args.Length>0 && args[0]=="viewport") return ViewportApp.Run("Board Game Builder — Player viewport probe",args[1..]);
        if(args.Length==2 && args[0]=="table") return ViewportApp.Run("",[],()=>TablePreview.Create(args[1]));
        return PlayerCli.Run(args);
    }
}
