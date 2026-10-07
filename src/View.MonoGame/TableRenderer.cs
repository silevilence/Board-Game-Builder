using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Drawing = System.Drawing;
namespace BoardGame.View;

public sealed class TableRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly SpriteBatch batch;
    private readonly Texture2D pixel;
    private readonly Dictionary<string, Texture2D> labels = [];
    private readonly Dictionary<string,Texture2D> images=[];
    private readonly Dictionary<string,Rectangle> imageContent=[];
    public int CachedLabels=>labels.Count;
    public int LabelTexturesCreated { get; private set; }
    public TableRenderer(GraphicsDevice device)
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
        var cacheKey=size+":"+text;
        if (!labels.TryGetValue(cacheKey, out var texture))
        {
            // Bound GPU memory even when labels contain arbitrary module text.
            if (labels.Count >= 256) return;
            using var font=new Drawing.Font("Microsoft YaHei UI", size, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel);
            using var measure=new Drawing.Bitmap(1,1);
            using var measureGraphics=Drawing.Graphics.FromImage(measure);
            var measured=measureGraphics.MeasureString(text,font);
            using var bitmap=new Drawing.Bitmap(Math.Max(1,(int)Math.Ceiling(measured.Width)),Math.Max(24,(int)Math.Ceiling(measured.Height)));
            using (var graphics=Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(Drawing.Color.Transparent);
                graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(text,font,Drawing.Brushes.White,0,0);
            }
            using var stream=new MemoryStream(); bitmap.Save(stream,Drawing.Imaging.ImageFormat.Png); stream.Position=0;
            texture=Texture2D.FromStream(device,stream); LabelTexturesCreated++; labels[cacheKey]=texture;
        }
        batch.Draw(texture,new Vector2(x,y),color);
    }
    public void End() => batch.End();
    public void Image(string id,IReadOnlyDictionary<string,byte[]> resources,Rectangle bounds,bool trimTransparent=false)
    {
        if(!resources.TryGetValue(id,out var bytes)) { Text("缺失资源: "+id,bounds.X,bounds.Y,Color.Red); return; }
        var key=id+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if(!images.TryGetValue(key,out var texture))
        {
            try { using var stream=new MemoryStream(bytes); texture=Texture2D.FromStream(device,stream); images.Add(key,texture); }
            catch(Exception) { Text("资源无法解码: "+id,bounds.X,bounds.Y,Color.Red); return; }
        }
        Rectangle? source=null;
        if(trimTransparent)
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
        batch.Draw(texture,bounds,source,Color.White);
    }
    public void Dispose() { foreach(var t in labels.Values.Concat(images.Values)) t.Dispose(); labels.Clear(); images.Clear(); imageContent.Clear(); pixel.Dispose(); batch.Dispose(); }
}
