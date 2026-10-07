using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace BoardGame.Editor;

public sealed record ImportPreview(string Revision,JsonObject Configuration,Dictionary<string,byte[]> Files,List<string> Report)
{
    public void Apply(ModuleDocument document)=>document.Import(Revision,Configuration,Files);
}

[SupportedOSPlatform("windows6.1")]
public static class ImageImport
{
    public static ImportPreview Preview(ModuleDocument document,string file,string prefix,int rows=1,int columns=1,bool tablecloth=false)
    {
        if(string.IsNullOrWhiteSpace(prefix) || prefix.Length>60 || prefix.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new InvalidDataException("名称仅支持字母、数字、横线和下划线，最多 60 字符。");
        if(rows<1 || columns<1 || rows* (long)columns>200 || tablecloth && (rows!=1 || columns!=1)) throw new InvalidDataException("切分数量必须为 1～200；桌布不能切分。");
        var info=new FileInfo(file);if(info.Length>16*1024*1024) throw new InvalidDataException("图片超过 16 MiB。");
        using var image=new Bitmap(file);
        if(image.Width>8192 || image.Height>8192 || (long)image.Width*image.Height>32_000_000 || image.Width%columns!=0 || image.Height%rows!=0)
            throw new InvalidDataException("图片尺寸超限或不能整除行列。");
        var root=document.Configuration;var files=new Dictionary<string,byte[]>();var report=new List<string>();
        var resources=root["resources"]!.AsObject();var entities=root["entities"]!.AsArray();var ids=new List<string>();
        var width=image.Width/columns;var height=image.Height/rows;
        for(var r=0;r<rows;r++) for(var c=0;c<columns;c++)
        {
            var id=$"{prefix}-{r*columns+c+1:D3}";var path=$"assets/{id}.png";
            if(resources.ContainsKey(id) || entities.Any(e=>e!["id"]!.GetValue<string>()==id) || document.Paths.Any(p=>p.Equals(path,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("导入命名冲突："+id);
            using var crop=image.Clone(new Rectangle(c*width,r*height,width,height),PixelFormat.Format32bppArgb);using var data=new MemoryStream();crop.Save(data,ImageFormat.Png);
            files.Add(path,data.ToArray());resources.Add(id,path);ids.Add(id);
            var components=new JsonArray {new JsonObject {["type"]="faces",["data"]=new JsonObject {["names"]=new JsonArray(id),["up"]=true,["resources"]=new JsonArray(id)}},
                new JsonObject {["type"]="position",["data"]=new JsonObject {["x"]=c*90,["y"]=r*130}}};
            if(tablecloth) components.Add(new JsonObject {["type"]="properties",["data"]=new JsonObject {["viewTablecloth"]=1,["viewWidth"]=image.Width,["viewHeight"]=image.Height}});
            entities.Add(new JsonObject {["id"]=id,["components"]=components});report.Add($"{id} · {width}×{height} · {path}");
        }
        if(!tablecloth)
        {
            var deck=prefix+"-deck";
            if(entities.Any(e=>e!["id"]!.GetValue<string>()==deck)) throw new InvalidDataException("牌组命名冲突："+deck);
            entities.Add(new JsonObject {["id"]=deck,["components"]=new JsonArray {new JsonObject {["type"]="container",["data"]=new JsonObject {["capacity"]=ids.Count,["ordered"]=true,["contents"]=new JsonArray(ids.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray())}}}});
        }
        return new(document.Revision,root,files,report);
    }
}
