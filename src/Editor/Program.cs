using BoardGame.View;
namespace BoardGame.Editor;
internal static class Program
{
    [STAThread] private static int Main(string[] args) => args.FirstOrDefault()=="--smoke" ? ViewportApp.Run("Editor viewport probe",args) : ViewportApp.Run("",args,()=>new EditorWindow(args.FirstOrDefault()));
}
