using BoardGame.View;
namespace BoardGame.Player;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="viewport") return ViewportApp.Run("Board Game Builder — Player viewport probe",args[1..]);
        if(args.Length==2 && args[0]=="table") return ViewportApp.Run("",[],()=>TablePreview.Create(args[1]));
        return PlayerCli.Run(args);
    }
}
