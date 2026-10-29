using System.Runtime.Versioning;
using BoardGame.Authoring;
namespace BoardGame.Editor;
[SupportedOSPlatform("windows6.1")]
public static class TtsImport
{
    public static ImportPreview Preview(ModuleDocument document,string file,string prefix)=>
        ImportPreview.From(document,BoardTtsImport.Preview(ImportPreview.Source(document),file,prefix));
}
