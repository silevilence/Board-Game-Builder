using BoardGame.Kernel;
namespace BoardGame.View;

public sealed record TableRect(float X,float Y,float Width,float Height)
{
    public bool Contains(float x,float y) => x>=X && y>=Y && x<X+Width && y<Y+Height;
}
public sealed record TableItem(string? Entity, string Kind, string Label, TableRect Bounds, string? Resource=null,double PositionX=0,double PositionY=0,BoardLayout? Layout=null,string? Detail=null,string? Container=null,int Hotkey=0,int Mark=0,float SlotHeight=0,int Color=0xE3C887,int Layer=0,int LineX=0,int LineY=0,int LabelY=0,float Rotation=0,string? Action=null,IReadOnlyList<string>? ActionIds=null,int TextSize=13)
{
    public (double X,double Y) MoveBy(float dx,float dy)
    {
        if(Layout==null) return (PositionX+dx,PositionY+dy);
        var world=Layout.World(PositionX,PositionY); var logical=Layout.Logical(world.X+dx,world.Y+dy); return Layout.Snap(logical.X,logical.Y);
    }
}
public sealed record TableScene(IReadOnlyList<TableItem> Items, ResourcePool Resources,BoardLayout? Layout=null,bool ClassicCards=false,IReadOnlyDictionary<string,int>? AnimationCues=null)
{
    public TableItem? Hit(float x,float y) => Items.LastOrDefault(i=>(i.Entity!=null || i.Action!=null) && i.Bounds.Contains(x,y));
    public TableItem? Region(float x,float y) => Items.FirstOrDefault(i=>i.Kind=="region" && i.Bounds.Contains(x,y));
    public static TableScene From(SeatView projection,ResourcePool? resources=null)
    {
        var items=new List<TableItem>();var used=new HashSet<string>(StringComparer.Ordinal);
        void Use(string? value) {if(value!=null && resources!=null) {_=resources.GetImage(value);used.Add(value);}}
        foreach(var cloth in projection.Entities.Where(e=>e.Properties.GetValueOrDefault("viewTablecloth")==1 && e.Resource!=null))
        {
            Use(cloth.Resource);
            items.Add(new(null,"tablecloth","",new((float)cloth.X,(float)cloth.Y,Math.Clamp(cloth.Properties.GetValueOrDefault("viewWidth",1200),1,8192),Math.Clamp(cloth.Properties.GetValueOrDefault("viewHeight",800),1,8192)),cloth.Resource));
        }
        var regions=projection.Entities.Where(e=>e.IsContainer && e.Properties.GetValueOrDefault("viewHidden")!=1).OrderBy(e=>e.Owner==projection.Seat?1:0).ToArray();
        var classic=regions.Any(e=>e.Properties.GetValueOrDefault("viewTheme")==1);
        var index=0;
        foreach(var region in regions)
        {
            var point=projection.Layout?.World(region.X,region.Y);
            var x=point==null?30+index%2*400:(float)point.Value.X; var y=point==null?40+index/2*235:(float)point.Value.Y; index++;
            // Presentation properties are generic media settings; ordering comes only from the projection.
            var horizontal=region.Properties.ContainsKey("viewFanX");
            var fan=region.Properties.ContainsKey("viewFanY");
            float Setting(string key,int fallback,int min,int max)=>Math.Clamp(region.Properties.GetValueOrDefault(key,fallback),min,max);
            var cardWidth=Setting("viewCardWidth",65,30,300); var cardHeight=Setting("viewCardHeight",105,40,400);
            var fanY=Setting("viewFanY",28,0,100); var regionWidth=Setting("viewWidth",380,40,2000);
            var regionHeight=Setting("viewHeight",215,40,4000);
            if(fan) regionHeight=Math.Max(regionHeight,(classic?0:36)+cardHeight+Math.Max(0,region.Count-1)*fanY);
            items.Add(new(region.Id,"region",classic?region.Face ?? region.Id:$"{region.Face ?? region.Id}  ·  {region.Count}",new(x,y,regionWidth,regionHeight),PositionX:region.X,PositionY:region.Y,Layout:projection.Layout,Hotkey:region.Properties.GetValueOrDefault("viewHotkey"),Mark:region.Properties.GetValueOrDefault("viewSuit"),SlotHeight:cardHeight));
            var visibleChildren=projection.Entities.Where(e=>e.Container==region.Id).ToDictionary(e=>e.Id);
            var children=region.Contents.Where(visibleChildren.ContainsKey).Select(id=>visibleChildren[id]).ToArray();
            if(region.Properties.GetValueOrDefault("viewTopOnly")==1) children=children.TakeLast(1).ToArray();
            Use(region.Resource);
            var fanX=Math.Min(Setting("viewFanX",30,0,300),Math.Max(0,(regionWidth-cardWidth-28)/Math.Max(1,region.Count-1)));
            // Anonymous backs encode only the public count, never authority identities.
            for(var j=0;j<Math.Min(12,region.Count-visibleChildren.Count);j++)
                items.Add(new(null,"back","隐藏",new(x+14+j*(horizontal?fanX:20),y+42,cardWidth,cardHeight),region.Resource));
            for(var j=0;j<children.Length;j++)
            {
                var e=children[j]; var resource=e.Resource;
                Use(resource);
                var bounds=horizontal?new TableRect(x+14+j*fanX,y+36,cardWidth,cardHeight):fan?new TableRect(x+(classic?0:6),y+(classic?0:30)+j*fanY,cardWidth,cardHeight):new TableRect(x+14+j%5*70,y+42+j/5*48,65,105);
                items.Add(new(e.Id,e.Face==null?"back":"card",e.Face ?? "背面",bounds,resource,Container:region.Id));
            }
        }
        var loose=0;
        foreach(var e in projection.Entities.Where(e=>!e.IsContainer && e.Container==null && e.Properties.GetValueOrDefault("viewHidden")!=1 && e.Properties.GetValueOrDefault("viewTablecloth")!=1))
        {
            var label=e.Face ?? e.Id;
            // Data-driven labels and sprites keep game-specific UI out of the shared renderer.
            var style=e.Properties.GetValueOrDefault("viewStyle");
            if(style is >=1 and <=3)
            {
                foreach(var p in e.Properties) label=label.Replace("{"+p.Key+"}",p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var pos=projection.Layout?.World(e.X,e.Y) ?? (e.X,e.Y);
                var w=Math.Clamp(e.Properties.GetValueOrDefault("viewWidth",48),4,2000);
                var h=Math.Clamp(e.Properties.GetValueOrDefault("viewHeight",48),4,2000);
                var resource=e.Properties.GetValueOrDefault("viewNoImage")==1?null:e.Resource;
                Use(resource);
                items.Add(new(e.Properties.GetValueOrDefault("viewPassive")==1?null:e.Id,style==2?"line":style==3?"panel":"sprite",
                    e.Properties.GetValueOrDefault("viewLabel")==1?label:"",new((float)pos.Item1-w/2f,(float)pos.Item2-h/2f,w,h),resource,e.X,e.Y,projection.Layout,
                    Color:e.Properties.GetValueOrDefault("viewColor",0xE3C887),Layer:e.Properties.GetValueOrDefault("viewLayer"),
                    LineX:Math.Clamp(e.Properties.GetValueOrDefault("viewLineX"),-4000,4000),LineY:Math.Clamp(e.Properties.GetValueOrDefault("viewLineY"),-4000,4000),
                    LabelY:e.Properties.GetValueOrDefault("viewLabelY",h/2+2)));
                continue;
            }
            var detail=e.Properties.Count>0?string.Join(" ",e.Properties.Select(p=>$"{p.Key}:{p.Value}")):null;
            if(projection.Layout==null && detail!=null) label+="  "+detail;
            Use(e.Resource);
            var point=projection.Layout?.World(e.X,e.Y);
            var bounds=point==null?new TableRect((float)e.X+30,(float)e.Y+40+(regions.Length+1)/2*235+loose++*48,360,40):new TableRect((float)point.Value.X-36,(float)point.Value.Y-24,76,48);
            items.Add(new(e.Id,"piece",label,bounds,e.Resource,e.X,e.Y,projection.Layout,projection.Layout==null?null:detail));
        }
        SceneCaptions.Append(resources,items);
        var cues=resources?.Animations?.Clips.ToDictionary(c=>c.Id,c=>projection.Entities.FirstOrDefault(e=>e.Id==c.Trigger.Entity)?.Properties.GetValueOrDefault(c.Trigger.Property) ?? 0);
        return new(items.OrderBy(i=>i.Layer).ToArray(),resources?.Select(used) ?? ResourcePool.Empty,projection.Layout,classic,cues);
    }
}
