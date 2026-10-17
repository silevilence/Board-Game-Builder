using SkiaSharp;
using BoardGame.Kernel;

namespace BoardGame.View;

// Device-local owner. Rasterized labels never retain a native typeface after disposal.
public sealed class PackagedTypeface : IDisposable
{
    private SKTypeface? typeface;
    private ResourcePool? source;
    public string Identity {get;private set;}="";
    public string? Family=>typeface?.FamilyName;
    public void Configure(ResourcePool resources)
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
    public byte[] Render(string text,int size)
    {
        if(typeface==null)throw new InvalidOperationException("No packaged font configured");
        using var font=new SKFont(typeface,Math.Clamp(size,6,128)) {Edging=SKFontEdging.Antialias};
        if(!font.ContainsGlyphs(text))throw new InvalidDataException("Packaged font has missing glyphs: "+text);
        using var paint=new SKPaint {Color=SKColors.White,IsAntialias=true};
        var metrics=font.Metrics;var width=Math.Clamp((int)Math.Ceiling(font.MeasureText(text))+4,1,16384);
        var height=Math.Clamp((int)Math.Ceiling(metrics.Descent-metrics.Ascent)+4,1,256);
        using var bitmap=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Premul);
        using(var canvas=new SKCanvas(bitmap)) {canvas.Clear(SKColors.Transparent);canvas.DrawText(text,2,2-metrics.Ascent,SKTextAlign.Left,font,paint);}
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();
    }
    public void Dispose() {typeface?.Dispose();typeface=null;source=null;Identity="";}
}
