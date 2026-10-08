using BoardGame.View;
namespace BoardGame.Player;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length==3 && args[0]=="--verify-showcase") return ViewportApp.Run("",[],()=>ShowcaseVerification.Create(args[1],args[2]));
        if(args.Length==3 && args[0]=="--verify-online-gui") return ViewportApp.Run("",[],()=>ShowcaseVerification.Online(args[1],args[2]));
        if(args.Length==2 && args[0]=="--verify-local") return ViewportApp.Run("",[],()=>LocalTableVerification.Create(args[1]));
        if(args.Length==2 && args[0]=="--verify-background") return ViewportApp.Run("",[],()=>new PlayerWindow("samples/quick-cards",verifyBackground:args[1],shell:PlayerShell.Tools));
        if(args.Length==2 && args[0]=="--verify-shell") return ViewportApp.Run("",[],()=>new PlayerWindow("samples/quick-cards",verifyShell:args[1]));
#if EDITOR_DEBUG
        if(args.FirstOrDefault()=="--editor-debug")
        {
            if(args.Contains("--headless")) { BoardGame.Runtime.DebugTransport.Serve(); return 0; }
            return ViewportApp.Run("",[],()=>new DebugPlayerWindow());
        }
#endif
        if(args.Length==0 || args[0]=="gui")
        {
            PlayerLaunch launch;
            try {launch=PlayerLaunch.Parse(args.Length==0?[]:args[1..]);}
            catch(ArgumentException error) {Console.Error.WriteLine(error.Message);return 2;}
            return ViewportApp.Run("",[],()=>new PlayerWindow(launch.Module,launch.Smoke,launch.Actions,shell:launch.Shell));
        }
        if(args.Length>0 && args[0]=="viewport") return ViewportApp.Run("Board Game Builder — Player viewport probe",args[1..]);
        if(args.Length==2 && args[0]=="table") return ViewportApp.Run("",[],()=>TablePreview.Create(args[1]));
        return PlayerCli.Run(args);
    }
}
