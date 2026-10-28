using BoardGame.Resources;
using BoardGame.Rendering;
using Microsoft.Xna.Framework;
namespace BoardGame.View;

// Legacy drawing syntax over the frame-scoped generic API; owns no GPU objects.
public sealed class TableRenderer(IDraw2D drawing,Func<RenderMetrics> metrics) : IDraw2D
{
    public int CachedImages=>metrics().Images;
    public int CachedLabels=>metrics().Labels;
    public int LabelTexturesCreated=>metrics().LabelsCreated;
    public void ConfigureResources(AssetSet resources)=>drawing.ConfigureResources(resources);
    public void Box(Rectangle bounds,Color color)=>drawing.Box(Rect(bounds),Tint(color));
    public void Line(Vector2 from,Vector2 to,Color color)=>drawing.Line(new(from.X,from.Y),new(to.X,to.Y),Tint(color));
    public void Text(string text,int x,int y,Color color,int size=13)=>drawing.Text(text,x,y,Tint(color),size);
    public void Image(string id,AssetSet resources,Rectangle bounds,bool trimTransparent=false,float rotation=0)=>drawing.Image(id,resources,Rect(bounds),trimTransparent,rotation);
    private static DrawRect Rect(Rectangle value)=>new(value.X,value.Y,value.Width,value.Height);
    private static DrawColor Tint(Color value)=>new(value.R,value.G,value.B,value.A);
    void IDraw2D.Box(DrawRect bounds,DrawColor color)=>drawing.Box(bounds,color);
    void IDraw2D.Line(DrawPoint from,DrawPoint to,DrawColor color)=>drawing.Line(from,to,color);
    void IDraw2D.Text(string text,int x,int y,DrawColor color,int size)=>drawing.Text(text,x,y,color,size);
    void IDraw2D.Image(string name,AssetSet resources,DrawRect bounds,bool trimTransparent,float rotation)=>drawing.Image(name,resources,bounds,trimTransparent,rotation);
}
