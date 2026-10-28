using Avalonia;
using BoardGame.Kernel;
using BoardGame.Presentation;
using BoardGame.Resources;

AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
static void Check(bool value,string message){if(!value)throw new Exception(message);}
var assets=new AssetSet(new Dictionary<string,ReadOnlyMemory<byte>>{{"paper",File.ReadAllBytes("samples/freecell/assets/card_clubs_A.png")}},
    new Dictionary<string,ImageReference>{{"page",new("paper")}},new Dictionary<string,string>());
PagePanel[] content=[new("guide","Guide",[new("One","page",300,450,[new(20,30,Text:"Move with the arrow keys")]),new("Two","page",300,450,[])],true)];
using var shelf=new PageShelf(assets,content);shelf.Show(content);
Check(shelf.VisibleText.SequenceEqual(new[]{"Move with the arrow keys"}),"Plain content was not laid out");
shelf.Turn(1);shelf.Show(content);Check(shelf.CurrentPage==1 && shelf.VisibleText.Count==0,"Refresh reset navigation");
try{shelf.Show([]);throw new Exception("Silently changed page structure");}catch(InvalidOperationException){}
shelf.Turn(-1);Check(shelf.VisibleText.Count==1,"Invalid update damaged existing content");
shelf.Dispose();shelf.Show(content);Check(shelf.VisibleText.Count==0,"Closed layout revived");
foreach(var dependency in typeof(PageShelf).Assembly.GetReferencedAssemblies())
    Check(dependency.Name is not ("Kernel" or "Kernel.Abstractions" or "View.Abstractions" or "Runtime" or "Lua"),"Generic layout depends on board types");
Console.WriteLine("PASS generic Avalonia page layout, navigation, replacement rejection and disposal without board assembly");
