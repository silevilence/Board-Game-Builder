using BoardGame.Resources;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
namespace BoardGame.Rendering;

public sealed class GlDraw2D : IDraw2D, IDisposable
{
    private readonly GraphicsDevice device;
    private readonly SpriteBatch batch;
    private readonly Texture2D pixel;
    private readonly Dictionary<string, Texture2D> labels = [];
    private readonly Dictionary<string,Texture2D> images=[];
    private readonly Dictionary<string,Rectangle> imageContent=[];
    private readonly GlTypeface packagedFont=new();
    public string? FontFamily=>packagedFont.Family;
    public void ConfigureResources(AssetSet resources)=>packagedFont.Configure(resources);
    public int CachedImages=>images.Count;
    public int CachedLabels=>labels.Count;
    public int LabelTexturesCreated { get; private set; }
    public GlDraw2D(GraphicsDevice device)
    {
        this.device=device; batch=new(device); pixel=new(device,1,1); pixel.SetData(new[] { Color.White });
    }
    public void Begin(float zoom, float x, float y)
    {
        // Dynamic score/resource labels must not disappear once a long game fills the cache.
        // Retire textures between batches, never while SpriteBatch still references them.
        if(labels.Count>=192) {foreach(var label in labels.Values) label.Dispose();labels.Clear();}
        batch.Begin(blendState:BlendState.NonPremultiplied,transformMatrix: Matrix.CreateScale(zoom)*Matrix.CreateTranslation(x,y,0));
    }
    public void Box(Rectangle bounds, Color color) => batch.Draw(pixel,bounds,color);
    public void Line(Vector2 from,Vector2 to,Color color) => batch.Draw(pixel,from,null,color,MathF.Atan2(to.Y-from.Y,to.X-from.X),Vector2.Zero,new Vector2(Vector2.Distance(from,to),1),SpriteEffects.None,0);
    public void Text(string text, int x, int y, Color color,int size=13)
    {
        text=text.Length>150 ? text[..150] : text;
        var cacheKey=packagedFont.Identity+":"+size+":"+text;
        if (!labels.TryGetValue(cacheKey, out var texture))
        {
            // Bound GPU memory even when labels contain arbitrary module text.
            if (labels.Count >= 256) return;
            if(packagedFont.Family!=null)
            {
                using var png=new MemoryStream(packagedFont.Render(text,size));
                texture=Texture2D.FromStream(device,png);LabelTexturesCreated++;labels[cacheKey]=texture;
                batch.Draw(texture,new Vector2(x,y),color);return;
            }
            using var stream=new MemoryStream(GlTypeface.RenderDefault(text,size));
            texture=Texture2D.FromStream(device,stream); LabelTexturesCreated++; labels[cacheKey]=texture;
        }
        batch.Draw(texture,new Vector2(x,y),color);
    }
    public void End() => batch.End();
    public void Image(string id,AssetSet resources,Rectangle bounds,bool trimTransparent=false,float rotation=0)
    {
        var reference=resources.GetImage(id);var bytes=resources.GetFile(reference.Resource);
        var key=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Span));
        if(!images.TryGetValue(key,out var texture))
        {
            try { using var stream=new MemoryStream(bytes.ToArray()); texture=Texture2D.FromStream(device,stream); images.Add(key,texture); }
            catch(Exception) { Text("资源无法解码: "+id,bounds.X,bounds.Y,Color.Red); return; }
        }
        reference.CheckBounds(texture.Width,texture.Height);
        Rectangle? source=reference.IsRegion?new(reference.X,reference.Y,reference.Width,reference.Height):null;
        if(trimTransparent && !reference.IsRegion)
        {
            if(!imageContent.TryGetValue(key,out var content))
            {
                var pixels=new Color[texture.Width*texture.Height]; texture.GetData(pixels);
                var left=texture.Width; var top=texture.Height; var right=-1; var bottom=-1;
                for(var y=0;y<texture.Height;y++) for(var x=0;x<texture.Width;x++)
                    if(pixels[y*texture.Width+x].A>0) { left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y); }
                content=right<left?new(0,0,texture.Width,texture.Height):new(left,top,right-left+1,bottom-top+1);
                imageContent[key]=content;
            }
            source=content;
        }
        var destination=reference.Destination(bounds.X,bounds.Y,bounds.Width,bounds.Height);
        var sw=source?.Width ?? texture.Width;var sh=source?.Height ?? texture.Height;
        batch.Draw(texture,new Vector2(destination.X+destination.Width/2,destination.Y+destination.Height/2),source,Color.White,
            MathHelper.ToRadians(rotation)-(reference.Rotated?MathHelper.PiOver2:0),new Vector2(sw/2f,sh/2f),
            reference.Rotated?new Vector2(destination.Height/sw,destination.Width/sh):new Vector2(destination.Width/sw,destination.Height/sh),SpriteEffects.None,0);
    }
    void IDraw2D.Box(DrawRect bounds,DrawColor color)=>Box(new(bounds.X,bounds.Y,bounds.Width,bounds.Height),Convert(color));
    void IDraw2D.Line(DrawPoint from,DrawPoint to,DrawColor color)=>Line(new(from.X,from.Y),new(to.X,to.Y),Convert(color));
    void IDraw2D.Text(string text,int x,int y,DrawColor color,int size)=>Text(text,x,y,Convert(color),size);
    void IDraw2D.Image(string name,AssetSet assets,DrawRect bounds,bool trimTransparent,float rotation)=>Image(name,assets,new(bounds.X,bounds.Y,bounds.Width,bounds.Height),trimTransparent,rotation);
    private static Color Convert(DrawColor color)=>new(color.R,color.G,color.B,color.A);
    public void Dispose() { foreach(var t in labels.Values.Concat(images.Values)) t.Dispose(); labels.Clear(); images.Clear(); imageContent.Clear(); packagedFont.Dispose(); pixel.Dispose(); batch.Dispose(); }
}
