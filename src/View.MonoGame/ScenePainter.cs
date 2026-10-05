using Microsoft.Xna.Framework;
namespace BoardGame.View;
public static class ScenePainter
{
    public static void Draw(TableRenderer renderer,TableScene scene,string? selected=null,string? target=null)
    {
        foreach(var item in scene.Items)
        {
            var b=item.Bounds;
            var color=item.Kind switch { "region"=>new Color(27,66,72),"back"=>new Color(63,89,114),"piece"=>new Color(34,49,60),_=>new Color(234,209,157) };
            if(item.Entity!=null && item.Entity==target) color=new Color(47,114,97);
            if(item.Entity!=null && item.Entity==selected) renderer.Box(new((int)b.X-3,(int)b.Y-3,(int)b.Width+6,(int)b.Height+6),new Color(255,196,74));
            renderer.Box(new((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height),color);
            if(item.Resource!=null) renderer.Image(item.Resource,scene.Resources,new((int)b.X+3,(int)b.Y+25,(int)b.Width-6,(int)b.Height-28));
            renderer.Text(item.Label,(int)b.X+5,(int)b.Y+5,item.Kind=="card"?new Color(25,40,46):Color.White);
        }
    }
}
