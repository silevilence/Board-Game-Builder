using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Kernel;
using BoardGame.View;

AppBuilder.Configure<ViewportApp>().UsePlatformDetect().SetupWithoutStarting();
var fontFile=Path.GetFullPath(args.FirstOrDefault() ?? "samples/cursed-investigators/assets/fonts/noto-sans-cjk-sc/NotoSansCJKsc-Regular.otf");
var license=Path.Combine(Path.GetDirectoryName(fontFile)!,"LICENSE.txt");
var pool=new ResourcePool(new Dictionary<string,ReadOnlyMemory<byte>>{{"font.otf",File.ReadAllBytes(fontFile)},{"LICENSE.txt",File.ReadAllBytes(license)}},new Dictionary<string,ImageReference>(),new Dictionary<string,string>(),new("font.otf","LICENSE.txt"));
var text="调查日志：幸运、决心、知识 0123456789，。！？";
using(var face=new PackagedTypeface())
{
    face.Configure(pool);var first=face.Render(text,24);
    if(face.Family!="Noto Sans CJK SC" || first.Length<100)throw new Exception("Packaged font failed");
    face.Configure(ResourcePool.Empty);if(face.Family!=null)throw new Exception("Font leaked after reset");
    face.Configure(pool);if(!first.SequenceEqual(face.Render(text,24)))throw new Exception("Font reload changed rendering");
    if(args.Length>1)File.WriteAllBytes(args[1],first);
}
var one=new TextBlock();var two=new TextBlock();
using(var first=new PackagedControlFont(one,pool))
using(var second=new PackagedControlFont(two,pool))
{
    var old=one.FontFamily;
    if(!FontManager.Current.TryGetGlyphTypeface(new Typeface(old),out var glyph))throw new Exception("UI font not registered");
    if(!glyph.CharacterToGlyphMap.TryGetGlyph('调',out var code) || code==0)throw new Exception("UI Chinese glyph missing");
    first.Dispose();
    if(!FontManager.Current.TryGetGlyphTypeface(new Typeface(two.FontFamily),out _))throw new Exception("Disposing one surface removed another font");
    if(FontManager.Current.TryGetGlyphTypeface(new Typeface(old),out _))throw new Exception("Disposed font registration retained");
}
Console.WriteLine("PASS packaged font rasterization, UI glyph loading, reset and independent disposal");
