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
    public TableRenderer(GraphicsDevice device)
    {
        this.device=device; batch=new(device); pixel=new(device,1,1); pixel.SetData(new[] { Color.White });
    }
    public void Begin(float zoom, float x, float y) => batch.Begin(transformMatrix: Matrix.CreateScale(zoom)*Matrix.CreateTranslation(x,y,0));
    public void Box(Rectangle bounds, Color color) => batch.Draw(pixel,bounds,color);
    public void Text(string text, int x, int y, Color color)
    {
        if (!labels.TryGetValue(text, out var texture))
        {
            // Bound GPU memory even when labels contain arbitrary module text.
            if (labels.Count >= 256) return;
            text=text.Length>150 ? text[..150] : text;
            using var font=new Drawing.Font("Microsoft YaHei UI", 13, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel);
            using var measure=new Drawing.Bitmap(1,1);
            using var measureGraphics=Drawing.Graphics.FromImage(measure);
            var size=measureGraphics.MeasureString(text,font);
            using var bitmap=new Drawing.Bitmap(Math.Max(1,(int)Math.Ceiling(size.Width)),24);
            using (var graphics=Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(Drawing.Color.Transparent);
                graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(text,font,Drawing.Brushes.White,0,0);
            }
            using var stream=new MemoryStream(); bitmap.Save(stream,Drawing.Imaging.ImageFormat.Png); stream.Position=0;
            texture=Texture2D.FromStream(device,stream); labels[text]=texture;
        }
        batch.Draw(texture,new Vector2(x,y),color);
    }
    public void End() => batch.End();
    public void Dispose() { foreach(var t in labels.Values) t.Dispose(); labels.Clear(); pixel.Dispose(); batch.Dispose(); }
}
