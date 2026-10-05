using BoardGame.View;
namespace BoardGame.Player;
internal static class Program
{
    [STAThread] private static int Main(string[] args) => args.Length>0 && args[0]=="viewport"
        ? ViewportApp.Run("Board Game Builder — Player viewport probe",args[1..]) : PlayerCli.Run(args);
}
