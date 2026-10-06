using Microsoft.Xna.Framework;
namespace BoardGame.View;
public static class ScenePainter
{
    public static void Draw(TableRenderer renderer,TableScene scene,string? selected=null,string? target=null,string? peeked=null)
    {
        if(scene.ClassicCards) { DrawClassic(renderer,scene,selected,target,peeked); return; }
        if(scene.Layout is {Kind:not "free"} grid)
            for(var q=-2;q<10;q++) for(var r=-2;r<8;r++)
            {
                var p=grid.World(q,r); var vertices=grid.Kind=="hex"?6:4; var radius=grid.Kind=="hex"?grid.Size:grid.Size/Math.Sqrt(2);
                for(var i=0;i<vertices;i++)
                {
                    var angle=(grid.Kind=="hex"?30:45)+i*360.0/vertices; var next=angle+360.0/vertices;
                    Vector2 At(double a)=>new((float)(p.X+radius*Math.Cos(a*Math.PI/180)),(float)(p.Y+radius*Math.Sin(a*Math.PI/180)));
                    renderer.Line(At(angle),At(next),new Color(47,73,83));
                }
            }
        var peek=scene.Items.FirstOrDefault(i=>i.Entity==peeked && i.Kind=="card");
        foreach(var item in peek==null?scene.Items:scene.Items.Append(peek))
        {
            var b=item.Bounds;
            var color=item.Kind switch { "region"=>new Color(27,66,72),"back"=>new Color(63,89,114),"piece"=>new Color(34,49,60),_=>new Color(234,209,157) };
            if(item.Entity!=null && item.Entity==target) color=new Color(47,114,97);
            if(item.Entity!=null && item.Entity==selected) renderer.Box(new((int)b.X-3,(int)b.Y-3,(int)b.Width+6,(int)b.Height+6),new Color(255,196,74));
            renderer.Box(new((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height),color);
            if(item.Resource!=null) renderer.Image(item.Resource,scene.Resources,item.Kind=="card"
                ?new((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height)
                :new((int)b.X+3,(int)b.Y+25,(int)b.Width-6,(int)b.Height-28),trimTransparent:item.Kind=="card");
            if(item.Kind!="card" || item.Resource==null) renderer.Text(item.Label,(int)b.X+5,(int)b.Y+5,item.Kind=="card"?new Color(25,40,46):Color.White);
            if(item.Detail!=null) renderer.Text(item.Detail,(int)b.X+3,(int)b.Y+25,new Color(186,211,221));
        }
    }
    private static void DrawClassic(TableRenderer r,TableScene scene,string? selected,string? target,string? peeked)
    {
        var pale=new Color(167,214,174); var dark=new Color(0,69,24); var light=new Color(63,165,86);
        var slots=scene.Items.Where(i=>i.Kind=="region").ToArray();
        var cells=slots.Where(i=>i.Hotkey==0 && i.Mark==0).ToArray();
        var homes=slots.Where(i=>i.Mark>0).ToArray();
        if(cells.Length>0) r.Text("空 当",(int)cells[0].Bounds.X,(int)cells[0].Bounds.Y-25,pale);
        if(homes.Length>0) r.Text("收 牌  ·  A → K",(int)homes[0].Bounds.X,(int)homes[0].Bounds.Y-25,pale);
        if(cells.Length>0 && homes.Length>0)
        {
            var cx=(int)((cells[^1].Bounds.X+cells[^1].Bounds.Width+homes[0].Bounds.X)/2);
            r.Text("♔",cx-30,(int)cells[0].Bounds.Y+20,new Color(206,218,157),52);
            r.Text("FREECELL",cx-34,(int)cells[0].Bounds.Y+86,pale,12);
        }
        foreach(var slot in slots)
        {
            var b=slot.Bounds; var x=(int)b.X; var y=(int)b.Y; var w=(int)b.Width; var h=(int)slot.SlotHeight;
            var top=slot.Hotkey==0;
            if(top || !scene.Items.Any(i=>i.Container==slot.Entity))
            {
                r.Box(new(x-1,y-1,w+2,h+2),dark);
                r.Box(new(x,y,w,h),new Color(0,112,39));
                r.Box(new(x+1,y+1,w-2,h-2),new Color(0,121,42));
                r.Box(new(x-1,y+h,w+3,1),light); r.Box(new(x+w,y-1,1,h+2),light);
                if(slot.Mark is >=1 and <=4)
                    r.Text(new[]{"♣","♦","♥","♠"}[slot.Mark-1],x+22,y+30,new Color(44,151,73),40);
            }
            if(slot.Hotkey>0) r.Text(slot.Hotkey.ToString(),x+w/2-5,y-24,new Color(92,170,110),12);
            if(slot.Entity==target) Outline(r,new(x-3,y-3,w+6,h+6),new Color(237,213,104),2);
        }
        var cards=scene.Items.Where(i=>i.Kind!="region");
        var peek=cards.FirstOrDefault(i=>i.Entity==peeked && i.Kind=="card");
        foreach(var item in peek==null?cards:cards.Append(peek)) DrawCard(r,scene,item,item.Entity==selected);
    }
    public static void DrawCard(TableRenderer r,TableScene scene,TableItem item,bool selected=false)
    {
        var b=item.Bounds; var rect=new Rectangle((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height);
        r.Box(new(rect.X+2,rect.Y+3,rect.Width,rect.Height),new Color(0,45,14,100));
        r.Box(rect,new Color(251,250,241));
        if(item.Resource!=null) r.Image(item.Resource,scene.Resources,rect,trimTransparent:true);
        else r.Text(item.Label,rect.X+4,rect.Y+4,Color.Black);
        if(selected) Outline(r,new(rect.X-2,rect.Y-2,rect.Width+4,rect.Height+4),new Color(255,223,91),2);
    }
    private static void Outline(TableRenderer r,Rectangle b,Color c,int width)
    {
        r.Box(new(b.X,b.Y,b.Width,width),c); r.Box(new(b.X,b.Bottom-width,b.Width,width),c);
        r.Box(new(b.X,b.Y,width,b.Height),c); r.Box(new(b.Right-width,b.Y,width,b.Height),c);
    }
}
