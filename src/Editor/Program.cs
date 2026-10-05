using BoardGame.View;
namespace BoardGame.Editor;
internal static class Program
{
    [STAThread] private static int Main(string[] args) => ViewportApp.Run("Board Game Builder — Editor viewport probe",args);
}
