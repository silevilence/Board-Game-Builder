using BoardGame.Editor;
using BoardGame.Modules;
using BoardGame.Kernel;
using System.Drawing;
using System.Drawing.Imaging;
namespace Engine.Tests;
public static class ImportTests
{
    public static void Tts() {if(OperatingSystem.IsWindowsVersionAtLeast(6,1)) TtsWindows();}
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static void TtsWindows()
    {
        var doc=ModuleDocument.Create();var initial=doc.Revision;
        var preview=TtsImport.Preview(doc,"samples/tts-subset/save.json","tts");
        Check.That(preview.Report.Any(r=>r.Contains("远程")) && preview.Report.Any(r=>r.Contains("Custom_Model")) && preview.Report.Any(r=>r.Contains("LuaScript")));
        Check.That(doc.Revision==initial);preview.Apply(doc);var module=doc.Snapshot();
        Check.That(module.Initial.Entities["tts-001"].X==100 && module.Initial.Entities["tts-001"].Y==200);
        Check.That(module.Initial.Entities["tts-002"].Contents.SequenceEqual(new[] {"tts-003","tts-004"}));
        Check.That(module.ResourceData!.Count==5);
        Check.That(doc.Bytes("imports/tts-source.json").SequenceEqual(File.ReadAllBytes("samples/tts-subset/save.json")));
        Check.Throws(()=>TtsImport.Preview(doc,"samples/tts-subset/save.json","tts"),"冲突");
        doc.Undo();Check.That(initial==doc.Revision);doc.Redo();
        var directory=Path.Combine(Path.GetTempPath(),"bgb-tts-"+Guid.NewGuid());
        try {doc.Save(directory);Check.That(ModuleDocument.Open(directory).Snapshot().Fingerprint==module.Fingerprint);}
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
    }
    public static void Images() {if(OperatingSystem.IsWindowsVersionAtLeast(6,1)) ImagesWindows();}
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static void ImagesWindows()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-import-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"cards.png");
            using(var image=new Bitmap(120,160)) {using var graphics=Graphics.FromImage(image);graphics.Clear(Color.Orange);graphics.FillRectangle(Brushes.Blue,60,80,60,80);image.Save(path,ImageFormat.Png);}
            var doc=ModuleDocument.Create();var initial=doc.Revision;var initialPaths=doc.Paths;
            var preview=ImageImport.Preview(doc,path,"cards",2,2);Check.That(preview.Files.Count==4 && doc.Revision==initial);
            preview.Apply(doc);var imported=doc.Snapshot();Check.That(imported.ResourceData!.Count==4 && imported.Initial.Entities["cards-deck"].Contents.Count==4);
            Check.Throws(()=>ImageImport.Preview(doc,path,"cards",2,2),"冲突");
            doc.Undo();Check.That(doc.Revision==initial && doc.Paths.SequenceEqual(initialPaths));doc.Redo();
            var saved=Path.Combine(folder,"module");doc.Save(saved);Check.That(ModuleDocument.Open(saved).Snapshot().Fingerprint==imported.Fingerprint);
            TestModules.Pack(saved,Path.Combine(folder,"cards.bgm"));Check.That(TestModules.Load(Path.Combine(folder,"cards.bgm")).ResourceData!.Count==4);
            Check.Throws(()=>ImageImport.Preview(doc,path,"bad",3,1),"整除");
            var stale=ImageImport.Preview(doc,path,"other");doc.EditStartup(root=>root["id"]="modified");Check.Throws(()=>stale.Apply(doc),"重新预览");
            var cloth=ImageImport.Preview(doc,path,"cloth",tablecloth:true);cloth.Apply(doc);
            var scene=BoardGame.View.TableScene.From(new GameEngine(doc.Snapshot().Initial).Project("p1"),doc.Snapshot().ResourceData);
            Check.That(scene.Items[0].Kind=="tablecloth" && scene.Items[0].Entity==null);
        }
        finally {Directory.Delete(folder,true);}
    }
}
