using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;
using BoardGame.Player;
using BoardGame.Runtime;
using BoardGame.View;

var output=Path.GetFullPath(args.FirstOrDefault()??".scratch/cursed-investigators/development/view");
Directory.CreateDirectory(output);
return ViewportApp.Run("Sample presentation verification",[],()=>
{
    var window=new PlayerWindow(Path.GetFullPath("samples/cursed-investigators"));
    if(args.Length>1)window.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.PlaceholderText=="种子").Text="1";
    window.Opened+=async (_,_)=>
    {
        string? error=null;var checks=new List<string>();
        try
        {
            PlayBoard? board=null;
            for(var wait=0;wait<400 && board==null;wait++) {await Task.Delay(50);board=window.GetLogicalDescendants().OfType<PlayBoard>().FirstOrDefault();}
            if(board==null)throw new Exception("Player board did not open");
            var table=board.GetLogicalDescendants().OfType<InteractiveTable>().Single();
            var shelf=board.GetLogicalDescendants().OfType<DocumentShelf>().Single();
            string? renderError=null;table.RenderingError+=message=>renderError=message;
            for(var wait=0;wait<100 && table.RenderedFrames==0 && renderError==null;wait++)await Task.Delay(100);
            if(table.RenderedFrames==0)throw new Exception("No initial frame: "+renderError+" bounds="+table.Bounds+" text="+string.Join(" | ",board.GetLogicalDescendants().OfType<TextBlock>().Select(t=>t.Text)));
            async Task Act(string kind)
            {
                for(var wait=0;table.AnimationBusy && wait<100;wait++)await Task.Delay(30);
                var action=board.Frame.Actions.First(a=>a.Command.Kind==kind);
                await board.Act(new("action",Action:action.Id),board.Frame.Revision);
                if(board.Frame.Fault!=null)throw new Exception(Wire.Json(board.Frame.Fault));
            }
            void Capture(Control control,string name)
            {
                var size=new PixelSize(Math.Max(1,(int)control.Bounds.Width),Math.Max(1,(int)control.Bounds.Height));
                using var bitmap=new RenderTargetBitmap(size,new Vector(96,96));bitmap.Render(control);bitmap.Save(Path.Combine(output,name+".png"),PngBitmapEncoderOptions.Default);
            }
            if(args.Length>1)
            {
                foreach(var command in Wire.Read<List<Command>>(File.ReadAllText(args[1])))
                {
                    while(table.AnimationBusy)await Task.Delay(30);
                    var offer=board.Frame.Actions.SingleOrDefault(a=>Wire.Json(a.Command)==Wire.Json(command))
                        ?? throw new Exception("Unavailable recorded command: "+Wire.Json(command));
                    await board.Act(new("action",Action:offer.Id),board.Frame.Revision);
                    if(board.Frame.Fault!=null)throw new Exception(Wire.Json(board.Frame.Fault));
                }
                if(!board.Frame.View.Ended || !board.Frame.View.Winners.Contains("p1"))throw new Exception("Recorded game did not win");
                await Task.Delay(100);table.SaveImage(Path.Combine(output,"winning-table.png"));checks.Add("complete-winning-player-game");
            }
            else
            {
                await Act("start");await Task.Delay(200);table.SaveImage(Path.Combine(output,"deal-moving.png"));
                await Task.Delay(1600);table.SaveImage(Path.Combine(output,"dealt.png"));
                await Act("target");await Act("ready");await Task.Delay(250);table.SaveImage(Path.Combine(output,"encounter.png"));
            }
            if(TableScene.From(board.Frame.View).Items.Any(i=>i.Kind=="region"))throw new Exception("Internal storage regions leaked onto table");
            if(table.RenderedFrames==0 || table.CachedImages<8)throw new Exception("Table did not render assets");
            checks.Add("real-player-deal-and-encounter-rendering");
            var before=Wire.Json(board.Frame.View);Capture(shelf,"journal-front");shelf.Turn(1);await Task.Delay(100);Capture(shelf,"journal-back");
            shelf.Show(board.Frame.View);if(shelf.CurrentPage!=1)throw new Exception("Refresh reset page");
            shelf.Select(1);await Task.Delay(200);
            var reference=window.OwnedWindows.Single();Capture((Control)reference.Content!,"reference");reference.Close();
            if(Wire.Json(board.Frame.View)!=before || shelf.CurrentPanel!="journal")throw new Exception("Document UI changed game or replaced permanent journal");
            checks.Add("journal-pages-and-reference-window-without-game-actions");
            window.Width=1100;window.Height=740;await Task.Delay(200);table.Fit();await Task.Delay(100);table.SaveImage(Path.Combine(output,"small-window.png"));
            checks.Add("resized-window-rendering");
        }
        catch(Exception exception){error=exception.ToString();}
        finally {window.Close();File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new{success=error==null,error,checks,devices=DesktopViewport.LiveDevices}));}
    };
    return window;
});

