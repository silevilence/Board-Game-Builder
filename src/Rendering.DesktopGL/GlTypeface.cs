using SkiaSharp;
using BoardGame.Resources;

namespace BoardGame.Rendering;

// Device-local owner. Rasterized labels never retain a native typeface after disposal.
public sealed class GlTypeface : IDisposable
{
    private SKTypeface? typeface;
    private AssetSet? source;
    public string Identity {get;private set;}="";
    public string? Family=>typeface?.FamilyName;
    public void Configure(AssetSet resources)
    {
        if(ReferenceEquals(source,resources))return;
        var bytes=resources.Font is {} reference?resources.GetFile(reference.Resource):ReadOnlyMemory<byte>.Empty;
        var hash=bytes.IsEmpty?"":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Span));
        if(hash!=Identity)
        {
            SKTypeface? next=null;
            if(!bytes.IsEmpty)
            {
                using var data=SKData.CreateCopy(bytes.Span);
                next=SKTypeface.FromData(data) ?? throw new InvalidDataException(resources.Font!.Resource+": font could not be decoded");
            }
            typeface?.Dispose();typeface=next;Identity=hash;
        }
        source=resources;
    }
    public static byte[] RenderDefault(string text,int size) {using var fallback=new GlTypeface {typeface=SKTypeface.FromFamilyName("Microsoft YaHei UI") ?? SKTypeface.Default};return fallback.Render(text,size);}
    public byte[] Render(string text,int size)
    {
        if(typeface==null)throw new InvalidOperationException("No packaged font configured");
        using var font=new SKFont(typeface,Math.Clamp(size,6,128)) {Edging=SKFontEdging.Antialias};
        var lines=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\t","    ",StringComparison.Ordinal).Split('\n');
        if(lines.Length>32)throw new InvalidDataException("Packaged font label exceeds 32 lines");
        if(lines.Any(line=>line.Length>0 && !font.ContainsGlyphs(line)))throw new InvalidDataException("Packaged font has missing glyphs: "+text);
        using var paint=new SKPaint {Color=SKColors.White,IsAntialias=true};
        var metrics=font.Metrics;var width=Math.Clamp((int)Math.Ceiling(lines.Max(line=>font.MeasureText(line)))+4,1,16384);
        var lineHeight=Math.Ceiling(metrics.Descent-metrics.Ascent+Math.Max(0,metrics.Leading));
        var height=Math.Clamp((int)(lineHeight*lines.Length)+4,1,8192);
        using var bitmap=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Premul);
        using(var canvas=new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            for(var i=0;i<lines.Length;i++)canvas.DrawText(lines[i],2,(float)(2-metrics.Ascent+i*lineHeight),SKTextAlign.Left,font,paint);
        }
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();
    }
    public void Dispose() {typeface?.Dispose();typeface=null;source=null;Identity="";}
}
