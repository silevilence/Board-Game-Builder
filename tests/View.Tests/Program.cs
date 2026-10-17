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
    using(var single=SkiaSharp.SKBitmap.Decode(face.Render("调查",24)))
    using(var multiline=SkiaSharp.SKBitmap.Decode(face.Render("调查\r\n\r\n知识\t6",24)))
        if(multiline.Height<single.Height*2.5)throw new Exception("Line breaks were treated as glyphs or line layout was lost");
    try {face.Render("\uFFFF",24);throw new Exception("Missing glyph silently accepted");}
    catch(InvalidDataException) { }
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
var library=new DocumentLibrary(1,[new("notes","Notes",[new("First","paper",600,900,[new(10,20,Binding:new("public","count"))]),new("Second","paper",600,900,[])],true)]);
var files=pool.Files.ToDictionary();files["paper.png"]=File.ReadAllBytes("samples/freecell/assets/card_clubs_A.png");
var paperPool=new ResourcePool(files,new Dictionary<string,ImageReference>{{"paper",new("paper.png")}},new Dictionary<string,string>(),pool.Font,documents:library);
var state=new GameState {Seats=[new("p1","one")],Phases=["play"],Phase="play",Entities=new(StringComparer.Ordinal){{"public",new(){Id="public",Properties=new(StringComparer.Ordinal){{"count",2}}}}}};
using(var shelf=new DocumentShelf(paperPool))
{
    var before=Wire.Json(state);shelf.Show(new GameEngine(state).Project("p1"));
    if(!shelf.VisibleText.Contains("2"))throw new Exception("Projected field missing");
    shelf.Turn(1);shelf.Show(new GameEngine(state).Project("p1"));
    if(shelf.CurrentPage!=1 || Wire.Json(state)!=before)throw new Exception("Navigation mutated state or reset after refresh");
    state.Ended=true;shelf.Show(new GameEngine(state).Project("p1"));shelf.Turn(-1);
    if(shelf.CurrentPage!=0 || !shelf.VisibleText.Contains("2"))throw new Exception("Documents unavailable after game end");
    shelf.Dispose();if(shelf.VisibleText.Count!=0)throw new Exception("Disposed document retained text");
}
Console.WriteLine("PASS document projection, page navigation, refresh, terminal state and disposal");
