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
        // Packed clockwise, then restored counterclockwise into its original transparent canvas.
        var metadata=System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(atlas.Resources!.GetFile("assets/cards.json").Span))!;
        metadata["frames"]!["card_clubs_A"]=System.Text.Json.Nodes.JsonNode.Parse("""
            {"frame":{"x":0,"y":0,"w":20,"h":10},"rotated":true,"trimmed":true,
             "spriteSourceSize":{"x":5,"y":6,"w":10,"h":20},"sourceSize":{"w":20,"h":30}}
            """);
        using var packed=new System.Drawing.Bitmap(1872,970);
        using(var paint=System.Drawing.Graphics.FromImage(packed))
        {
            paint.FillRectangle(System.Drawing.Brushes.Red,0,0,10,5);paint.FillRectangle(System.Drawing.Brushes.Lime,10,0,10,5);
            paint.FillRectangle(System.Drawing.Brushes.Blue,0,5,10,5);paint.FillRectangle(System.Drawing.Brushes.Yellow,10,5,10,5);
        }
        using var png=new MemoryStream();packed.Save(png,System.Drawing.Imaging.ImageFormat.Png);
        var image=atlas.Resources!.GetImage("card_clubs_A") with {X=0,Y=0,Width=20,Height=10,Rotated=true,OffsetX=5,OffsetY=6,SourceWidth=20,SourceHeight=30};
        var files=atlas.Resources.Files.ToDictionary(p=>p.Key,p=>p.Value);files["assets/cards.png"]=png.ToArray();
        var images=atlas.Resources.Images.ToDictionary(p=>p.Key,p=>p.Value);images["card_clubs_A"]=image;
        var pool=new BoardGame.Kernel.ResourcePool(files,images,atlas.Resources.Binaries);
        var transformed=PrintLayout.Create(atlas with {Resources=pool},["card-01"],new(Duplex:"none")).Pages[0][0];
        using var jpeg=new MemoryStream(transformed.Image);using var result=new System.Drawing.Bitmap(jpeg);
        void Pixel(int x,int y,System.Drawing.Color expected)
        {
            var actual=result.GetPixel(x*result.Width/20,y*result.Height/30);
            Check.That(Math.Abs(actual.R-expected.R)<30 && Math.Abs(actual.G-expected.G)<30 && Math.Abs(actual.B-expected.B)<30,"Atlas rotation/trim print mismatch");
        }
        Pixel(1,1,System.Drawing.Color.White);Pixel(7,9,System.Drawing.Color.Lime);Pixel(12,9,System.Drawing.Color.Yellow);
        Pixel(7,22,System.Drawing.Color.Red);Pixel(12,22,System.Drawing.Color.Blue);
    }
}
