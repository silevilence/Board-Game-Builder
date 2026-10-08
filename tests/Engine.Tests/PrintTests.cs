using BoardGame.Editor;
namespace Engine.Tests;
public static class PrintTests
{
    public static void Run() {if(OperatingSystem.IsWindowsVersionAtLeast(6,1)) Windows();}
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static void Windows()
    {
        var doc=ModuleDocument.Create();TtsImport.Preview(doc,"samples/tts-subset/save.json","print").Apply(doc);var module=doc.Snapshot();
        var ids=module.Initial.Entities.Values.Where(e=>!e.IsContainer && e.Properties.GetValueOrDefault("viewTablecloth")!=1).Select(e=>e.Id).ToArray();
        var selected=ids.Concat(ids).Take(7).ToArray();var plan=PrintLayout.Create(module,selected,new());Check.That(plan.Pages.Count==4);
        Check.That(plan.Pages[0].Count==6 && plan.Pages[2].Count==1);
        for(var page=0;page<4;page+=2)
        for(var i=0;i<plan.Pages[page].Count;i++)
        {
            var front=plan.Pages[page][i];var back=plan.Pages[page+1][i];Check.That(front.Entity==back.Entity && front.Width==63 && front.Height==88);
            Check.That(Math.Abs(front.X+back.X+front.Width-210)<0.001 && front.Y==back.Y);
        }
        var shortEdge=PrintLayout.Create(module,selected,new(Duplex:"short-edge"));Check.That(shortEdge.Pages[0][0].Y+shortEdge.Pages[1][0].Y+88==297);
        Check.Throws(()=>PrintLayout.Create(module,selected,new(CardWidth:250)),"无法");
        Directory.CreateDirectory("artifacts/m3");var destination="artifacts/m3/print-"+Guid.NewGuid()+".pdf";PrintLayout.Export(plan,destination);
        Check.Throws(()=>PrintLayout.Export(plan,destination),"已存在");
        File.WriteAllText("artifacts/m3/print-path.txt",Path.GetFullPath(destination));
        var atlas=BoardGame.Runtime.GameRuntime.Load("samples/poker-uno");
        var atlasPlan=PrintLayout.Create(atlas,["card-01","card-02"],new());
        Check.That(!atlasPlan.Pages[0][0].Image.SequenceEqual(atlasPlan.Pages[0][1].Image),"Atlas fronts were not cropped individually");
        Check.That(atlasPlan.Pages[1][0].Image.SequenceEqual(atlasPlan.Pages[1][1].Image),"Shared atlas backs differ");
        Directory.CreateDirectory("artifacts/poker-uno");
        PrintLayout.Export(atlasPlan,"artifacts/poker-uno/print-"+Guid.NewGuid()+".pdf");
    }
}
