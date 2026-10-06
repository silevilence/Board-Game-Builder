using BoardGame.Kernel;
namespace BoardGame.View;

public sealed record TableRect(float X,float Y,float Width,float Height)
{
    public bool Contains(float x,float y) => x>=X && y>=Y && x<X+Width && y<Y+Height;
}
public sealed record TableItem(string? Entity, string Kind, string Label, TableRect Bounds, string? Resource=null,double PositionX=0,double PositionY=0,BoardLayout? Layout=null,string? Detail=null,string? Container=null,int Hotkey=0,int Mark=0,float SlotHeight=0)
{
    public (double X,double Y) MoveBy(float dx,float dy)
    {
        if(Layout==null) return (PositionX+dx,PositionY+dy);
        var world=Layout.World(PositionX,PositionY); var logical=Layout.Logical(world.X+dx,world.Y+dy); return Layout.Snap(logical.X,logical.Y);
    }
}
public sealed record TableScene(IReadOnlyList<TableItem> Items, IReadOnlyDictionary<string,byte[]> Resources,BoardLayout? Layout=null,bool ClassicCards=false)
{
    public TableItem? Hit(float x,float y) => Items.LastOrDefault(i=>i.Entity!=null && i.Bounds.Contains(x,y));
    public TableItem? Region(float x,float y) => Items.FirstOrDefault(i=>i.Kind=="region" && i.Bounds.Contains(x,y));
    public static TableScene From(SeatView projection,IReadOnlyDictionary<string,byte[]>? resources=null)
    {
        var items=new List<TableItem>(); var used=new Dictionary<string,byte[]>();
        var regions=projection.Entities.Where(e=>e.IsContainer && e.Properties.GetValueOrDefault("viewHidden")!=1).OrderBy(e=>e.Owner==projection.Seat?1:0).ToArray();
        var classic=regions.Any(e=>e.Properties.GetValueOrDefault("viewTheme")==1);
        var index=0;
        foreach(var region in regions)
        {
            var point=projection.Layout?.World(region.X,region.Y);
            var x=point==null?30+index%2*400:(float)point.Value.X; var y=point==null?40+index/2*235:(float)point.Value.Y; index++;
            // Presentation properties are generic media settings; ordering comes only from the projection.
            var fan=region.Properties.ContainsKey("viewFanY");
            float Setting(string key,int fallback,int min,int max)=>Math.Clamp(region.Properties.GetValueOrDefault(key,fallback),min,max);
            var cardWidth=Setting("viewCardWidth",65,30,300); var cardHeight=Setting("viewCardHeight",105,40,400);
            var fanY=Setting("viewFanY",28,0,100); var regionWidth=Setting("viewWidth",380,40,2000);
            var regionHeight=Setting("viewHeight",215,40,4000);
            if(fan) regionHeight=Math.Max(regionHeight,(classic?0:36)+cardHeight+Math.Max(0,region.Count-1)*fanY);
            items.Add(new(region.Id,"region",classic?region.Face ?? region.Id:$"{region.Face ?? region.Id}  ·  {region.Count}",new(x,y,regionWidth,regionHeight),PositionX:region.X,PositionY:region.Y,Layout:projection.Layout,Hotkey:region.Properties.GetValueOrDefault("viewHotkey"),Mark:region.Properties.GetValueOrDefault("viewSuit"),SlotHeight:cardHeight));
            var visibleChildren=projection.Entities.Where(e=>e.Container==region.Id).ToDictionary(e=>e.Id);
            var children=region.Contents.Where(visibleChildren.ContainsKey).Select(id=>visibleChildren[id]).ToArray();
            // Anonymous backs encode only the public count, never authority identities.
            for(var j=0;j<Math.Min(12,region.Count-children.Length);j++)
                items.Add(new(null,"back","隐藏",new(x+14+j*20,y+42,65,105)));
            for(var j=0;j<children.Length;j++)
            {
                var e=children[j]; var resource=e.Resource;
                if(resource!=null && resources!=null && resources.TryGetValue(resource,out var bytes)) used[resource]=bytes;
                var bounds=fan?new TableRect(x+(classic?0:6),y+(classic?0:30)+j*fanY,cardWidth,cardHeight):new TableRect(x+14+j%5*70,y+42+j/5*48,65,105);
                items.Add(new(e.Id,e.Face==null?"back":"card",e.Face ?? "背面",bounds,resource,Container:region.Id));
            }
        }
        var loose=0;
        foreach(var e in projection.Entities.Where(e=>!e.IsContainer && e.Container==null && e.Properties.GetValueOrDefault("viewHidden")!=1))
        {
            var label=e.Face ?? e.Id;
            var detail=e.Properties.Count>0?string.Join(" ",e.Properties.Select(p=>$"{p.Key}:{p.Value}")):null;
            if(projection.Layout==null && detail!=null) label+="  "+detail;
            if(e.Resource!=null && resources!=null && resources.TryGetValue(e.Resource,out var bytes)) used[e.Resource]=bytes;
            var point=projection.Layout?.World(e.X,e.Y);
            var bounds=point==null?new TableRect((float)e.X+30,(float)e.Y+40+(regions.Length+1)/2*235+loose++*48,360,40):new TableRect((float)point.Value.X-36,(float)point.Value.Y-24,76,48);
            items.Add(new(e.Id,"piece",label,bounds,e.Resource,e.X,e.Y,projection.Layout,projection.Layout==null?null:detail));
        }
        return new(items,used,projection.Layout,classic);
    }
}
