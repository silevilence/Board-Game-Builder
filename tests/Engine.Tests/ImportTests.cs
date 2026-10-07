using BoardGame.Editor;
using BoardGame.Modules;
using BoardGame.Kernel;
using System.Drawing;
using System.Drawing.Imaging;
namespace Engine.Tests;
public static class ImportTests
{
    public static void Images() {if(OperatingSystem.IsWindowsVersionAtLeast(6,1)) ImagesWindows();}
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static void ImagesWindows()
    {
        var folder=Path.Combine(Path.GetTempPath(),"bgb-import-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"cards.png");
            using(var image=new Bitmap(120,160)) {using var graphics=Graphics.FromImage(image);graphics.Clear(Color.Orange);graphics.FillRectangle(Brushes.Blue,60,80,60,80);image.Save(path,ImageFormat.Png);}
            var doc=ModuleDocument.Create();var initial=doc.Revision;
            var preview=ImageImport.Preview(doc,path,"cards",2,2);Check.That(preview.Files.Count==4 && doc.Revision==initial);
            preview.Apply(doc);var imported=doc.Snapshot();Check.That(imported.ResourceData!.Count==4 && imported.Initial.Entities["cards-deck"].Contents.Count==4);
            Check.Throws(()=>ImageImport.Preview(doc,path,"cards",2,2),"冲突");
            doc.Undo();Check.That(doc.Revision==initial && doc.Paths.Count==3);doc.Redo();
            var saved=Path.Combine(folder,"module");doc.Save(saved);Check.That(ModuleDocument.Open(saved).Snapshot().Fingerprint==imported.Fingerprint);
            ModulePackage.Pack(saved,Path.Combine(folder,"cards.bgm"));Check.That(ModulePackage.Load(Path.Combine(folder,"cards.bgm")).ResourceData!.Count==4);
            Check.Throws(()=>ImageImport.Preview(doc,path,"bad",3,1),"整除");
            var stale=ImageImport.Preview(doc,path,"other");doc.EditConfiguration(root=>root["id"]="modified");Check.Throws(()=>stale.Apply(doc),"重新预览");
            var cloth=ImageImport.Preview(doc,path,"cloth",tablecloth:true);cloth.Apply(doc);
            var scene=BoardGame.View.TableScene.From(new GameEngine(doc.Snapshot().Initial).Project("p1"),doc.Snapshot().ResourceData);
            Check.That(scene.Items[0].Kind=="tablecloth" && scene.Items[0].Entity==null);
        }
        finally {Directory.Delete(folder,true);}
    }
}
