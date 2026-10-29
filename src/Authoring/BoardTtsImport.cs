using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace BoardGame.Authoring;

[SupportedOSPlatform("windows6.1")]
public static class BoardTtsImport
{
    public static BoardImportPreview Preview(BoardImportSource document,string file,string prefix)
    {
        if(string.IsNullOrWhiteSpace(prefix) || prefix.Length>60 || prefix.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new InvalidDataException("导入名称无效");
        if(new FileInfo(file).Length>8*1024*1024) throw new InvalidDataException("TTS 存档超过 8 MiB");
        var input=File.ReadAllBytes(file);if(input.Length>8*1024*1024) throw new InvalidDataException("TTS 存档超过 8 MiB");
        var save=JsonNode.Parse(input)!.AsObject();var root=document.Configuration;var entities=root["entities"]!.AsArray();var resources=new JsonObject();
        if(root["layout"]==null) root["layout"]=new JsonObject {["kind"]="free",["size"]=100};
        var files=new Dictionary<string,byte[]> {["imports/"+prefix+"-source.json"]=input};var report=new List<string> {"来源脚本不会执行；原始存档将保留在 imports。"};
        var basePath=Path.GetDirectoryName(Path.GetFullPath(file))!;var count=0;long resourceBytes=input.Length;
        foreach(var item in save["ObjectStates"]?.AsArray() ?? throw new InvalidDataException("缺少 ObjectStates")) Convert(item!.AsObject(),null,0);
        if(save["TableURL"]?.GetValue<string>() is {Length:>0} tableUrl)
        {
            var resource=ReadImage(tableUrl,prefix+"-table",1,1,0);
            if(resource!=null) Add(prefix+"-table",new JsonArray {Component("faces",new() {["names"]=new JsonArray("桌布"),["up"]=true,["resources"]=new JsonArray(resource)}),Component("properties",new() {["viewTablecloth"]=1,["viewWidth"]=1200,["viewHeight"]=800})});
        }
        foreach(var field in save.Where(p=>p.Key is not ("ObjectStates" or "TableURL" or "SaveName" or "VersionNumber"))) report.Add("未转换顶层字段："+field.Key);
        foreach(var path in files.Keys) if(document.Paths.Any(p=>p.Equals(path,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("导入路径冲突："+path);
        if(files.Sum(f=>(long)f.Value.Length)>64*1024*1024) throw new InvalidDataException("转换资源超过 64 MiB");
        return new(document,root,files,report,resources.ToDictionary(p=>p.Key,p=>p.Value!.GetValue<string>()));

        string? Convert(JsonObject item,JsonObject? inherited,int depth)
        {
            if(depth>2 || ++count>200) throw new InvalidDataException("仅支持 200 个卡牌／牌堆对象及一层牌堆");
            var type=item["Name"]?.GetValue<string>() ?? "unknown";var id=prefix+"-"+count.ToString("D3");
            if(type is not ("Card" or "CardCustom" or "Deck" or "DeckCustom")) {report.Add($"不支持对象 {item["GUID"]}：{type}（保留在原始输入）");return null;}
            foreach(var field in item.Where(p=>p.Key is not ("Name" or "GUID" or "Nickname" or "Transform" or "CardID" or "CustomDeck" or "DeckIDs" or "ContainedObjects"))) report.Add($"{id} 未转换字段：{field.Key}");
            var transform=item["Transform"]?.AsObject();var x=transform?["posX"]?.GetValue<double>() ?? 0;var y=transform?["posZ"]?.GetValue<double>() ?? 0;
            if(!double.IsFinite(x) || !double.IsFinite(y)) throw new InvalidDataException("无效位置");
            if(transform!=null && transform.Any(p=>p.Key is not ("posX" or "posZ"))) report.Add(id+"：仅映射 posX/posZ，忽略高度／旋转／缩放");
            var components=new JsonArray {Component("position",new() {["x"]=x*100,["y"]=y*100})};
            var decks=item["CustomDeck"]?.AsObject() ?? inherited;
            if(type is "Deck" or "DeckCustom")
            {
                var children=new JsonArray();
                if(item["ContainedObjects"] is JsonArray contained)
                {
                    foreach(var child in contained)
                        if(Convert(child!.AsObject(),decks,depth+1) is {} childId) children.Add(childId);
                    if(item["DeckIDs"] is JsonArray deckIds && deckIds.Count!=contained.Count) report.Add(id+"：DeckIDs 与 ContainedObjects 数量不一致，按 ContainedObjects 转换");
                }
                else foreach(var cardId in item["DeckIDs"]?.AsArray() ?? [])
                {
                    var card=new JsonObject {["Name"]=type=="Deck"?"Card":"CardCustom",["CardID"]=cardId!.DeepClone()};
                    if(Convert(card,decks,depth+1) is {} childId) children.Add(childId);
                }
                components.Add(Component("container",new() {["capacity"]=children.Count,["ordered"]=true,["contents"]=children}));
            }
            else
            {
                var number=item["CardID"]?.GetValue<int>() ?? 0;var custom=decks?[(number/100).ToString()]?.AsObject();
                var label=item["Nickname"]?.GetValue<string>();if(string.IsNullOrWhiteSpace(label)) label="Card "+number;
                var names=new JsonArray(label);var images=new JsonArray();
                if(custom!=null)
                {
                    var columns=custom["NumWidth"]?.GetValue<int>() ?? 1;var rows=custom["NumHeight"]?.GetValue<int>() ?? 1;
                    var face=ReadImage(custom["FaceURL"]?.GetValue<string>(),id+"-front",rows,columns,number%100);
                    if(face!=null)
                    {
                        images.Add(face);
                        var unique=custom["UniqueBack"]?.GetValue<bool>()==true;
                        var back=ReadImage(custom["BackURL"]?.GetValue<string>(),id+"-back",unique?rows:1,unique?columns:1,unique?number%100:0);
                        if(back!=null) {images.Add(back);names.Add("背面");}
                    }
                    foreach(var field in custom.Where(p=>p.Key is not ("FaceURL" or "BackURL" or "NumWidth" or "NumHeight" or "UniqueBack"))) report.Add($"{id} 未转换 CustomDeck 字段：{field.Key}");
                }
                else report.Add(id+"：没有本地卡图，保留文字卡牌");
                components.Add(Component("faces",new() {["names"]=names,["up"]=true,["resources"]=images}));
            }
            Add(id,components);report.Add($"转换 {type} → {id}，位置 ({x*100}, {y*100})");return id;
        }
        void Add(string id,JsonArray components)
        {
            if(entities.Any(e=>e!["id"]!.GetValue<string>()==id)) throw new InvalidDataException("对象命名冲突："+id);
            entities.Add(new JsonObject {["id"]=id,["components"]=components});
        }
        string? ReadImage(string? location,string id,int rows,int columns,int index)
        {
            if(string.IsNullOrWhiteSpace(location)) {report.Add(id+"：缺失图片");return null;}
            if(location.StartsWith("http",StringComparison.OrdinalIgnoreCase) || location.StartsWith("\\\\",StringComparison.Ordinal)) {report.Add(id+"：未下载远程资源 "+location);return null;}
            var path=Path.GetFullPath(Path.Combine(basePath,location));
            if(!path.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) {report.Add(id+"：缺失或位于存档目录外的资源 "+location);return null;}
            for(FileSystemInfo? info=new FileInfo(path);info!=null && info.FullName!=basePath;info=Directory.GetParent(info.FullName))
                if((info.Attributes & FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("不读取链接资源："+location);
            if(new FileInfo(path).Length>16*1024*1024 || rows<1 || columns<1 || rows*(long)columns>200 || index<0 || index>=rows*(long)columns) throw new InvalidDataException("卡图尺寸或索引无效");
            using var bitmap=new Bitmap(path);
            if((long)bitmap.Width*bitmap.Height>32_000_000 || bitmap.Width%columns!=0 || bitmap.Height%rows!=0) throw new InvalidDataException("卡图不能按声明整除或尺寸超限");
            using var tile=bitmap.Clone(new Rectangle(index%columns*bitmap.Width/columns,index/columns*bitmap.Height/rows,bitmap.Width/columns,bitmap.Height/rows),PixelFormat.Format32bppArgb);
            using var stream=new MemoryStream();tile.Save(stream,ImageFormat.Png);var output="assets/"+id+".png";
            if(resources.ContainsKey(id) || files.ContainsKey(output)) throw new InvalidDataException("资源命名冲突："+id);
            if(resourceBytes+stream.Length>64*1024*1024) throw new InvalidDataException("转换资源超过 64 MiB");
            resourceBytes+=stream.Length;resources.Add(id,output);files.Add(output,stream.ToArray());return id;
        }
    }
    private static JsonObject Component(string type,JsonObject data)=>new() {["type"]=type,["data"]=data};
}
