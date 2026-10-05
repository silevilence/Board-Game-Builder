using BoardGame.Kernel;
namespace BoardGame.View;

public sealed record TableRect(float X,float Y,float Width,float Height)
{
    public bool Contains(float x,float y) => x>=X && y>=Y && x<X+Width && y<Y+Height;
}
public sealed record TableItem(string? Entity, string Kind, string Label, TableRect Bounds, string? Resource=null,double PositionX=0,double PositionY=0,BoardLayout? Layout=null)
{
    public (double X,double Y) MoveBy(float dx,float dy)
    {
        if(Layout==null) return (PositionX+dx,PositionY+dy);
        var world=Layout.World(PositionX,PositionY); var logical=Layout.Logical(world.X+dx,world.Y+dy); return Layout.Snap(logical.X,logical.Y);
    }
}
public sealed record TableScene(IReadOnlyList<TableItem> Items, IReadOnlyDictionary<string,byte[]> Resources,BoardLayout? Layout=null)
{
    public TableItem? Hit(float x,float y) => Items.LastOrDefault(i=>i.Entity!=null && i.Bounds.Contains(x,y));
    public TableItem? Region(float x,float y) => Items.FirstOrDefault(i=>i.Kind=="region" && i.Bounds.Contains(x,y));
    public static TableScene From(SeatView projection,IReadOnlyDictionary<string,byte[]>? resources=null)
    {
        var items=new List<TableItem>(); var used=new Dictionary<string,byte[]>();
        var regions=projection.Entities.Where(e=>e.IsContainer).OrderBy(e=>e.Owner==projection.Seat?1:0).ToArray();
        var index=0;
        foreach(var region in regions)
        {
            var point=projection.Layout?.World(region.X,region.Y);
            var x=point==null?30+index%2*400:(float)point.Value.X; var y=point==null?40+index/2*235:(float)point.Value.Y; index++;
            items.Add(new(region.Id,"region",$"{region.Id}  ·  {region.Count}",new(x,y,380,215),PositionX:region.X,PositionY:region.Y,Layout:projection.Layout));
            var children=projection.Entities.Where(e=>e.Container==region.Id).ToArray();
            // Anonymous backs encode only the public count, never authority identities.
            for(var j=0;j<Math.Min(12,region.Count-children.Length);j++)
                items.Add(new(null,"back","隐藏",new(x+14+j*20,y+42,65,105)));
            for(var j=0;j<children.Length;j++)
            {
                var e=children[j]; var resource=e.Resource;
                if(resource!=null && resources!=null && resources.TryGetValue(resource,out var bytes)) used[resource]=bytes;
                items.Add(new(e.Id,e.Face==null?"back":"card",e.Face ?? "背面",new(x+14+j%5*70,y+42+j/5*48,65,105),resource));
            }
        }
        var loose=0;
        foreach(var e in projection.Entities.Where(e=>!e.IsContainer && e.Container==null))
        {
            var label=e.Face ?? e.Id;
            if(e.Properties.Count>0) label+="  "+string.Join(" ",e.Properties.Select(p=>$"{p.Key}:{p.Value}"));
            if(e.Resource!=null && resources!=null && resources.TryGetValue(e.Resource,out var bytes)) used[e.Resource]=bytes;
            var point=projection.Layout?.World(e.X,e.Y);
            var bounds=point==null?new TableRect((float)e.X+30,(float)e.Y+40+(regions.Length+1)/2*235+loose++*48,360,40):new TableRect((float)point.Value.X-30,(float)point.Value.Y-18,100,40);
            items.Add(new(e.Id,"piece",label,bounds,e.Resource,e.X,e.Y,projection.Layout));
        }
        return new(items,used,projection.Layout);
    }
}
