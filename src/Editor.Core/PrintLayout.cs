using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using BoardGame.Kernel;

namespace BoardGame.Editor;

public sealed record PrintOptions(double CardWidth=63,double CardHeight=88,double Margin=10,double Gap=3,string Duplex="long-edge",double PageWidth=210,double PageHeight=297);
public sealed record PrintPlacement(string Entity,bool Back,double X,double Y,double Width,double Height,byte[] Image,int PixelWidth,int PixelHeight);
public sealed record PrintPlan(PrintOptions Options,List<List<PrintPlacement>> Pages);

[SupportedOSPlatform("windows6.1")]
public static class PrintLayout
{
    public static PrintPlan Create(LoadedModule module,IReadOnlyList<string> selected,PrintOptions options)
    {
        var o=options;
        if(new[] {o.CardWidth,o.CardHeight,o.Margin,o.Gap,o.PageWidth,o.PageHeight}.Any(v=>!double.IsFinite(v)) ||
           o.CardWidth is <10 or >300 || o.CardHeight is <10 or >300 || o.Margin<0 || o.Gap<0 || o.PageWidth is <20 or >1000 || o.PageHeight is <20 or >1000 || o.Duplex is not ("none" or "long-edge" or "short-edge"))
            throw new InvalidDataException("无效的毫米尺寸、页边距或正反面方式");
        var columns=(int)Math.Floor((o.PageWidth-2*o.Margin+o.Gap)/(o.CardWidth+o.Gap));
        var rows=(int)Math.Floor((o.PageHeight-2*o.Margin+o.Gap)/(o.CardHeight+o.Gap));
        if(columns<1 || rows<1 || selected.Count is <1 or >200) throw new InvalidDataException("卡牌无法放入纸张或选择数量不在 1～200");
        if(o.CardWidth/25.4*150*(o.CardHeight/25.4*150)*selected.Count*(o.Duplex=="none"?1:2)>100_000_000) throw new InvalidDataException("打印图像总尺寸超限，请减少选择数量");
        var pages=new List<List<PrintPlacement>>();var capacity=rows*columns;
        for(var offset=0;offset<selected.Count;offset+=capacity)
        {
            var front=new List<PrintPlacement>();var back=new List<PrintPlacement>();
            for(var i=0;i<Math.Min(capacity,selected.Count-offset);i++)
            {
                var id=selected[offset+i];
                if(!module.Initial.Entities.TryGetValue(id,out var entity) || entity.IsContainer) throw new InvalidDataException("请选择有效卡牌："+id);
                var x=o.Margin+i%columns*(o.CardWidth+o.Gap);var y=o.Margin+i/columns*(o.CardHeight+o.Gap);
                front.Add(Placement(false,x,y));
                if(o.Duplex!="none") back.Add(Placement(true,o.Duplex=="long-edge"?o.PageWidth-x-o.CardWidth:x,o.Duplex=="short-edge"?o.PageHeight-y-o.CardHeight:y));
                PrintPlacement Placement(bool reverse,double px,double py)
                {
                    var width=(int)Math.Round(o.CardWidth/25.4*150);var height=(int)Math.Round(o.CardHeight/25.4*150);
                    using var bitmap=new Bitmap(width,height);using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.White);
                    var face=reverse?1:0;
                    var reference=entity.FaceResources.Count>face?ImageReference.Parse(entity.FaceResources[face]):null;
                    if(reference!=null && module.ResourceData?.TryGetValue(reference.Resource,out var resource)==true)
                    {
                        if(resource.Length>16*1024*1024) throw new InvalidDataException("卡图超过打印资源限制");
                        using var stream=new MemoryStream(resource);using var image=new Bitmap(stream);
                        if((long)image.Width*image.Height>32_000_000) throw new InvalidDataException("卡图尺寸超限");
                        reference.CheckBounds(image.Width,image.Height);
                        var source=reference.IsRegion?new Rectangle(reference.X,reference.Y,reference.Width,reference.Height):new Rectangle(0,0,image.Width,image.Height);
                        graphics.DrawImage(image,new Rectangle(0,0,width,height),source,GraphicsUnit.Pixel);
                    }
                    else
                    {
                        using var font=new Font("Microsoft YaHei",14);var title=reverse?"背面\n"+id:entity.Faces.FirstOrDefault() ?? id;
                        using var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
                        graphics.DrawString(title,font,Brushes.Black,new RectangleF(10,10,width-20,height-20),format);
                    }
                    using var output=new MemoryStream();bitmap.Save(output,ImageFormat.Jpeg);
                    return new(id,reverse,px,py,o.CardWidth,o.CardHeight,output.ToArray(),width,height);
                }
            }
            pages.Add(front);if(back.Count>0) pages.Add(back);
        }
        return new(o,pages);
    }
    public static void Export(PrintPlan plan,string destination)
    {
        var path=Path.GetFullPath(destination);if(File.Exists(path)) throw new IOException("PDF 已存在，请选择新路径");
        // A small image-only PDF uses the exact same millimetre rectangles as the preview.
        List<byte[]> objects=[[],[]];var pageIds=new List<int>();
        static byte[] Text(string value)=>Encoding.ASCII.GetBytes(value);
        static string Number(double value)=>value.ToString("0.#####",CultureInfo.InvariantCulture);
        static double Points(double mm)=>mm*72/25.4;
        foreach(var page in plan.Pages)
        {
            var resources=new StringBuilder();var content=new StringBuilder("0.25 w\n");var index=0;
            foreach(var item in page)
            {
                var name="I"+index++;var imageId=objects.Count+1;
                using var image=new MemoryStream();image.Write(Text($"<< /Type /XObject /Subtype /Image /Width {item.PixelWidth} /Height {item.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {item.Image.Length} >>\nstream\n"));image.Write(item.Image);image.Write(Text("\nendstream"));objects.Add(image.ToArray());
                resources.Append($"/{name} {imageId} 0 R ");
                var x=Number(Points(item.X));var y=Number(Points(plan.Options.PageHeight-item.Y-item.Height));var w=Number(Points(item.Width));var h=Number(Points(item.Height));
                content.Append($"q {w} 0 0 {h} {x} {y} cm /{name} Do Q\n{x} {y} {w} {h} re S\n");
            }
            var commands=Text(content.ToString());var contentId=objects.Count+1;
            objects.Add(Text($"<< /Length {commands.Length} >>\nstream\n{content}endstream"));
            var pageId=objects.Count+1;pageIds.Add(pageId);
            objects.Add(Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(Points(plan.Options.PageWidth))} {Number(Points(plan.Options.PageHeight))}] /Resources << /XObject << {resources} >> >> /Contents {contentId} 0 R >>"));
        }
        objects[0]=Text("<< /Type /Catalog /Pages 2 0 R >>");objects[1]=Text($"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(" ",pageIds.Select(id=>$"{id} 0 R"))}] >>");
        using var pdf=new MemoryStream();pdf.Write(Text("%PDF-1.4\n"));var positions=new List<long> {0};
        for(var i=0;i<objects.Count;i++) {positions.Add(pdf.Position);pdf.Write(Text($"{i+1} 0 obj\n"));pdf.Write(objects[i]);pdf.Write(Text("\nendobj\n"));}
        var xref=pdf.Position;pdf.Write(Text($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n"));
        foreach(var position in positions.Skip(1)) pdf.Write(Text(position.ToString("D10",CultureInfo.InvariantCulture)+" 00000 n \n"));
        pdf.Write(Text($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid()+".tmp";
        try {File.WriteAllBytes(temporary,pdf.ToArray());File.Move(temporary,path,false);}
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
}
