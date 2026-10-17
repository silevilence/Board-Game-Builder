using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;
using BoardGame.Player;
using BoardGame.Runtime;
using BoardGame.View;

var output=Path.GetFullPath(args.FirstOrDefault()??".scratch/cursed-investigators/development/view");
var scripted=args.Length>1 && args[1]!="-";
Directory.CreateDirectory(output);
return ViewportApp.Run("Sample presentation verification",[],()=>
{
    var window=new PlayerWindow(Path.GetFullPath(args.ElementAtOrDefault(2)??"samples/cursed-investigators"));
    if(scripted)window.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.PlaceholderText=="种子").Text="1";
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
            async Task Click(Control scope,string text)
            {
                Button? button=null;
                for(var wait=0;wait<300;wait++)
                {
                    button=scope.GetLogicalDescendants().OfType<Button>().SingleOrDefault(b=>b.IsVisible && (b.Content is TextBlock t?t.Text:b.Content?.ToString())==text);
                    if(button?.IsEnabled==true)break;await Task.Delay(50);
                }
                if(button?.IsEnabled!=true)throw new Exception("Missing or disabled button: "+text);
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
                for(var wait=0;scope is PlayerSetupView s && s.IsVisible && s.GetLogicalDescendants().OfType<Button>().Any(b=>b.Name=="SetupNext" && !b.IsEnabled) && wait<300;wait++)await Task.Delay(50);
                // A completed command rebuilds controls; allow the following layout/render pass before captures.
                await Task.Delay(150);
            }
            var setup=board.GetLogicalDescendants().OfType<PlayerSetupView>().Single();
            if(!setup.IsVisible || setup.CurrentStep!=0)throw new Exception("Title not shown first");
            await Task.Delay(150);Capture(window,"title");
            await Click(setup,"开始调查");Capture(window,"mode");
            if(!scripted)
            {
                await Click(setup,"模式：两局战役");await Click(setup,"启用初学者：允许奖励健康");
            }
            await Click(setup,"选择人物 →");
            if(!scripted)await Click(setup,"角色：塞莱斯特");
            Capture(window,"character");await Click(setup,"选择职业 →");
            if(!scripted)
            {
                await Click(setup,"职业：前侦探");await Click(setup,"返回");
                if(setup.CurrentStep!=2 || board.Frame.View.Entities.Single(e=>e.Id=="match").Properties["character"]!=1)throw new Exception("Back lost selected character");
                await Click(setup,"选择职业 →");
            }
            Capture(window,"profession");checks.Add("title-mode-character-profession-navigation-and-preview");
            if(scripted)
            {
                foreach(var command in Wire.Read<List<Command>>(File.ReadAllText(args[1])))
                {
                    while(table.AnimationBusy)await Task.Delay(30);
                    var offer=board.Frame.Actions.SingleOrDefault(a=>Wire.Json(a.Command)==Wire.Json(command))
                        ?? throw new Exception("Unavailable recorded command: "+Wire.Json(command));
                    if(command.Kind=="start")await Click(setup,"开始游戏");
                    else await board.Act(new("action",Action:offer.Id),board.Frame.Revision);
                    if(board.Frame.Fault!=null)throw new Exception(Wire.Json(board.Frame.Fault));
                }
                if(!board.Frame.View.Ended || !board.Frame.View.Winners.Contains("p1"))throw new Exception("Recorded game did not win");
                await Task.Delay(100);table.SaveImage(Path.Combine(output,"winning-table.png"));checks.Add("complete-winning-player-game");
            }
            else
            {
                await Click(setup,"开始游戏");
                for(var wait=0;table.RenderedFrames==0 && renderError==null && wait<150;wait++)await Task.Delay(50);
                if(table.RenderedFrames==0)throw new Exception("No game frame: "+renderError);
                table.SaveImage(Path.Combine(output,"deal-moving.png"));
                await Task.Delay(1600);table.SaveImage(Path.Combine(output,"dealt.png"));
                await Act("target");await Act("ready");await Task.Delay(250);table.SaveImage(Path.Combine(output,"encounter.png"));
                var settings=board.Frame.View.Entities.Single(e=>e.Id=="match").Properties;
                if(settings["campaign"]!=1 || settings["beginner"]!=1 || settings["character"]!=1 || settings["profession"]!=2)throw new Exception("Setup choices not applied");
            }
            if(setup.IsVisible)throw new Exception("Setup remained above game");
            if(board.GetLogicalDescendants().OfType<DockPanel>().Any(p=>p.Width==250 && p.IsVisible))throw new Exception("Right action sidebar still visible");
            if(!scripted)
            {
                var tray=board.GetLogicalDescendants().OfType<WrapPanel>().Single(p=>p.IsEffectivelyVisible && p.Children.OfType<Button>().Any(b=>b.Content?.ToString()==board.Frame.Actions.First().Label));
                var labels=tray.Children.OfType<Button>().Select(b=>b.Content?.ToString()).ToHashSet();
                if(board.Frame.Actions.Any(a=>!labels.Contains(a.Label)))throw new Exception("Legal actions missing from bottom tray");
                var stableBounds=table.Bounds;var changedBounds=false;
                void BoundsChanged(object? sender,AvaloniaPropertyChangedEventArgs e){if(e.Property==Control.BoundsProperty && table.Bounds!=stableBounds)changedBounds=true;}
                table.PropertyChanged+=BoundsChanged;
                var revision=board.Frame.Revision;await Click(tray,board.Frame.Actions.First().Label);
                for(var wait=0;board.Frame.Revision==revision && wait<150;wait++)await Task.Delay(50);
                table.PropertyChanged-=BoundsChanged;
                if(board.Frame.Revision!=revision+1 || board.Frame.Fault!=null)throw new Exception("Bottom tray did not submit exactly one action");
                if(changedBounds)throw new Exception("Submitting an action resized the native viewport");
                checks.Add("stable-viewport-during-action-submission");
            }
            Capture(window,"game-window");checks.Add("hidden-right-actions-and-visible-bottom-tray");
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
            if(scripted)
            {
                Capture(window,"result");await Click(board,"返回标题");
                PlayBoard? next=null;
                for(var wait=0;wait<400 && (next==null || ReferenceEquals(next,board));wait++)
                {await Task.Delay(50);next=window.GetLogicalDescendants().OfType<PlayBoard>().FirstOrDefault();}
                if(next==null || ReferenceEquals(next,board) || next.Frame.View.Ended || next.Frame.View.Entities.Single(e=>e.Id=="match").Properties["score"]!=0
                    || next.GetLogicalDescendants().OfType<PlayerSetupView>().Single().CurrentStep!=0)throw new Exception("Result did not return to a clean title");
                await Task.Delay(300);
                var nextSetup=next.GetLogicalDescendants().OfType<PlayerSetupView>().Single();
                if(!nextSetup.IsEffectivelyVisible || nextSetup.Bounds.Width<100 || nextSetup.Bounds.Height<100)throw new Exception("Returned title did not lay out");
                Capture(window,"returned-title");checks.Add("terminal-result-return-to-fresh-title");
            }
        }
        catch(Exception exception){error=exception.ToString();}
        finally {window.Close();File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new{success=error==null,error,checks,devices=DesktopViewport.LiveDevices}));}
    };
    return window;
});

