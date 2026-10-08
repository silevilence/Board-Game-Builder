using BoardGame.Runtime;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using BoardGame.Kernel;
namespace BoardGame.Player;

public sealed partial class PlayerWindow
{
    private async Task VerifyShell(string output)
    {
        var results=new List<object>();
        static void Check(bool pass,string error) {if(!pass) throw new InvalidOperationException(error);}
        try
        {
            foreach(var name in new[] {"freecell","island-settlers","quick-cards"})
            foreach(var selected in new[] {PlayerShell.Tools,PlayerShell.Game})
            {
                SetShell(selected);path.Text="samples/"+name;await Load();await Task.Delay(120);
                Check(module!=null && start.IsEnabled,message.Text ?? "Module failed to load");
                Check(top.IsVisible==(selected==PlayerShell.Tools),"Theme controls the toolbar");
                Check(activeSeed.HasValue==(selected==PlayerShell.Game),"Theme controls automatic start");
                if(selected==PlayerShell.Tools) await Start();
                Check(activeSeed==42 && (board!=null || localBoard!=null),"Game failed to start");
                Check((board?.ActionsVisible ?? localBoard!.ActionsVisible)==presentation.ShowActions,"Actions do not follow presentation metadata");
                Check(name=="freecell"?Title!.StartsWith("空当接龙"):!Title!.Contains("空当接龙"),"Stale game title");
                Check(name!="quick-cards" || presentation.Help==null,"Stale module help");
                if(name=="island-settlers") Check(localBoard?.Frame.WaitingFor=="p1" && localBoard.LiveTables==0,"Default hotseat curtain missing");
                var current=play.Content;SetShell(selected==PlayerShell.Game?PlayerShell.Tools:PlayerShell.Game);
                Check(ReferenceEquals(current,play.Content),"Changing shell replaced the game");
                results.Add(new {module=name,shell=selected.ToString(),title=Title,success=true});
            }
            SetShell(PlayerShell.Game);path.Text="samples/island-settlers";await Load();
            var existing=localBoard;var revision=existing!.Frame.Revision;
            var cancelled=ShowSettings();await Task.Delay(80);
            Check(settingsDialog!=null,"Settings did not open");settingsDialog!.Seed.Text="99";settingsDialog.Close();await cancelled;
            Check(ReferenceEquals(existing,localBoard) && existing.Frame.Revision==revision && activeSeed==42,"Cancel changed the game");
            var pending=ShowSettings();await Task.Delay(80);var dialog=settingsDialog!;
            dialog.Seed.Text="invalid";dialog.Confirm();
            Check(settingsOpen && ReferenceEquals(existing,localBoard),"Invalid seed closed settings or replaced game");
            dialog.Seed.Text="77";foreach(var item in dialog.Humans.Values) item.IsChecked=false;
            dialog.Confirm();Check(settingsOpen && ReferenceEquals(existing,localBoard),"Empty humans replaced the game");
            dialog.Humans["p2"].IsChecked=true;dialog.Humans["p4"].IsChecked=true;dialog.Mode.SelectedIndex=0;
            dialog.Confirm();Check(settingsOpen && ReferenceEquals(existing,localBoard),"Multiple humans accepted in single-player mode");
            dialog.Mode.SelectedIndex=1;dialog.Confirm();await pending;
            Check(activeSeed==77 && localHumans.Text=="p2,p4" && localBoard!=null && !ReferenceEquals(existing,localBoard),"Settings did not start new mixed game");
            Check(localBoard!.Frame.WaitingFor=="p2" && localBoard.LiveTables==0,"CPU / human handoff incorrect");
            await localBoard.VerifyStep();await Task.Delay(160);
            Check(localBoard.LiveTables==1 && localBoard.Frame.Views[0].View.Seat=="p2" && localBoard.RenderedFrames>0,"Human projection did not render");
            localBoard.ToggleActions();Check(!localBoard.ActionsVisible,"Cannot hide local actions");
            localBoard.ToggleActions();Check(localBoard.ActionsVisible,"Cannot restore local actions");
            seed.Text="bad";existing=localBoard;await Start();Check(ReferenceEquals(existing,localBoard) && message.IsVisible,"Invalid toolbar settings destroyed game");seed.Text="77";
            await HostKey("restart");Check(activeSeed==77 && localBoard!.Frame.WaitingFor=="p2","Restart lost settings");
            Stop();Check(play.Content==null && message.IsVisible,"Stop did not expose recovery");await Start();
            // Classic cards must also be allowed to use the selected local-session mode.
            path.Text="samples/freecell";await Load();ApplySetup(new(42,1,["p1"]));await Start();
            Check(classic && localBoard!=null && board==null,"Classic theme forced single-player session");
            path.Text="samples/does-not-exist";await Load();
            Check(module==null && play.Content==null && message.IsVisible && !Title!.Contains("空当接龙"),"Load failure left stale presentation");
            path.Text="samples/quick-cards";await Load();Check(board!=null && presentation.Title=="quick-cards","Cannot recover after failed load");
            var fallback=PlayerPresentation.Read(module!);Check(fallback.Title==module!.Id && fallback.ShowActions,"Presentation defaults failed");
            var package=System.IO.Path.GetFullPath("artifacts/shell-"+Guid.NewGuid()+".bgm");
            BoardGame.Runtime.GamePackage.Pack("samples/island-settlers",package);path.Text=package;await Load();
            Check(localBoard!=null && presentation.Title=="群岛开拓" && !top.IsVisible,"Package presentation / automatic start failed");
            Check(presentation.Help?.Contains("秘密竞拍")==true,"Package help did not load");
            var bad=module with {ResourceData=new(StringComparer.Ordinal) {{"player.presentation",System.Text.Encoding.UTF8.GetBytes("{\"unknown\":true}")}}};
            var rejected=false;try {PlayerPresentation.Read(bad);}catch(System.Text.Json.JsonException) {rejected=true;}Check(rejected,"Malformed presentation was silently accepted");
            var launch=PlayerLaunch.Parse(["--shell","tools","samples/freecell"]);Check(launch.Shell==PlayerShell.Tools && launch.Module=="samples/freecell","GUI options incorrectly parsed");
            foreach(var args in new[] {new[] {"--shell","bad"},new[] {"--shell"},new[] {"--shell","game","--shell","tools"},new[] {"--unknown","x"},new[] {"--actions","x"}})
            {rejected=false;try {PlayerLaunch.Parse(args);}catch(ArgumentException) {rejected=true;}Check(rejected,"Invalid GUI arguments accepted");}
            results.Add(new {settings="cancel, invalid, mixed CPU, restart, classic hotseat, recovery, metadata, CLI",success=true});
            File.WriteAllText(output,Wire.Json(new {success=true,results}));
        }
        catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString(),results}));}
        finally {settingsDialog?.Close();Close();}
    }
    private async Task VerifyBackground(string output)
    {
        var results=new List<object>();
        var app=Application.Current!;
        var previous=app.RequestedThemeVariant;
        try
        {
            foreach(var theme in new[] {ThemeVariant.Light,ThemeVariant.Dark})
            {
                app.RequestedThemeVariant=theme;
                foreach(var name in new[] {"quick-cards","freecell","island-settlers"})
                {
                    path.Text="samples/"+name;
                    await Load();
                    await Task.Delay(100);
                    if(module==null) throw new InvalidOperationException(message.Text ?? "Module failed to load");
                    var brush=Background as ISolidColorBrush;
                    results.Add(new {module=name,theme=theme.ToString(),background=brush?.Color.ToString(),opacity=Opacity});
                    if(brush==null || brush.Color.A!=255 || brush.Opacity!=1 || Opacity!=1)
                        throw new InvalidOperationException($"{name} / {theme}: Player window background is not opaque");
                    if(classic && brush.Color!=Color.Parse("#d4d0c8"))
                        throw new InvalidOperationException("FreeCell classic background changed");
                    if(!classic && RequestedThemeVariant!=ThemeVariant.Default)
                        throw new InvalidOperationException("Standard Player does not follow the application theme");
                }
            }
            File.WriteAllText(output,Wire.Json(new {success=true,results}));
        }
        catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString(),results}));}
        finally {app.RequestedThemeVariant=previous;Close();}
    }
}
