using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using BoardGame.Rendering;

namespace BoardGame.Editor;

internal static class BackendVerification
{
    public static Window Create(string output,string player,string fixture)
    {
        Directory.CreateDirectory(output);var host=new EditorHost("samples/motion-lab");
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();var app=host.Application;
            Window? preview=null;ApplicationViewport? one=null,two=null;
            void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
            async Task Drawn(PlatformViewport view,int before=0)
            {for(var i=0;i<200;i++){if(view.Error!=null)throw new InvalidDataException(view.Error);if(view.Frames>before)return;await Task.Delay(25);}throw new TimeoutException("No viewport frame");}
            try
            {
                await host.Ready;var documents=app.Service<DocumentWorkspace>("documents");var sessions=app.Service<ApplicationSessions>("sessions");var views=app.Service<EditorViewports>("viewports");
                var store=app.Service<BoardGame.Plugins.PluginStore>("runtime.plugins");store.Import(Path.Combine(fixture,"counter"));
                var configuration=app.Service<RuntimeConfiguration>("runtime.configuration");configuration.Select(configuration.Available().Single(p=>p.Definition.Id=="sample.counter"));
                var original=File.ReadAllBytes("samples/motion-lab/game.start.json");
                void Backend(params string[] ids)
                {
                    var root=JsonNode.Parse(documents.Draft("game.start.json").Text)!.AsObject();var plugins=root["plugins"]!.AsArray();
                    foreach(var item in plugins.Where(p=>p!["id"]!.GetValue<string>().StartsWith("render.",StringComparison.Ordinal)).ToArray())plugins.Remove(item);
                    foreach(var id in ids)plugins.Add(new JsonObject{["id"]=id,["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
                    documents.EditDraft("game.start.json",root.ToJsonString());
                }
                Backend("render.windowsdx");await sessions.Start();Check(sessions.Active,sessions.Status);await sessions.Send("pause");
                var instance=sessions.Instance!;var source=sessions.SourceView!.Source.Version;var tick=sessions.State!.Tick;
                var grid=new Grid{ColumnDefinitions=new("*,*")};one=views.Create(instance);two=views.Create(instance);Grid.SetColumn(two,1);grid.Children.Add(one);grid.Children.Add(two);
                preview=new Window{Title="Backend switch verification",Width=1050,Height=600,Content=grid};preview.Show();await Drawn(one);await Drawn(two);
                foreach(var backend in new[]{"render.desktopgl","render.windowsdx","render.desktopgl"})
                {
                    Backend(backend);var before=one.Frames;await views.Apply();Check(!views.Status.Contains("失败"),views.Status);await Drawn(one,before);await Drawn(two);
                    Check(ReferenceEquals(instance,sessions.Instance) && sessions.SourceView!.Source.Version==source,"Graphics rebuild replaced the runtime snapshot");
                    await sessions.Send("status");Check(sessions.State!.Tick==tick,"Viewport rebuild advanced a paused instance");
                    Check(one.ActiveBackend==backend && two.ActiveBackend==backend,"Viewports disagree with selected backend");
                    one.SaveImage(Path.Combine(output,backend+".png"));
                }
                await sessions.Send("step");Check(sessions.State!.Tick==tick+1,"Two viewports added an update owner");
                var counter=instance.Service<object>("sample.counter");Check((int)counter.GetType().GetProperty("Ticks")!.GetValue(counter)! ==tick+1,"Imported runtime update diverged during backend switching");
                Check(WindowsDxBackend.LiveDevices==0 && WindowsDxBackend.LiveBackends==0,"DX resources retained after GL switch");
                checks.Add("dx-gl-dx-gl-two-viewports-no-instance-restart-no-duplicate-update-old-snapshot-retained");
                var dx=WindowsDxBackend.Plugin();var conflict=new PlatformViewport(new(new(1,"graphics","1",[new(dx.Id,dx.Version,dx.Api,BoardGame.Kernel.RuntimeConfiguration.Empty)]),[dx]));
                grid.Children.Add(conflict);for(var i=0;i<100 && conflict.Error==null;i++)await Task.Delay(20);
                Check(conflict.Error?.Contains("different backend")==true,"Shared window accepted conflicting document backend");conflict.Dispose();grid.Children.Remove(conflict);await conflict.Shutdown;
                Backend("render.windowsdx","render.desktopgl");await views.Apply();Check(views.Status.Contains("exactly one") && one.ActiveBackend=="render.desktopgl","Ambiguous backend silently switched");
                Backend("render.windowsdx");var changing=views.Apply();await views.CancelAndWait();await changing;Check(views.Status.Contains("失败"),"Canceled graphics switch succeeded");
                await views.Apply();await Drawn(one,one.Frames);Check(views.Backend=="render.windowsdx","Cannot retry after cancellation");
                documents.Undo();Check(documents.Draft("game.start.json").Text.Contains("render.desktopgl"),"Backend draft lost Undo history");documents.Redo();
                checks.Add("shared-window-conflict-ambiguous-selection-cancel-retry-undo");
                one.Dispose();two.Dispose();grid.Children.Clear();await Task.WhenAll(one.Shutdown,two.Shutdown);preview.Close();preview=null;
                Backend("render.desktopgl");await views.Apply();
                sessions.External=true;sessions.Executable=player;await sessions.Start();Check(sessions.Active,sessions.Status);await sessions.Send("pause");
                var externalTick=sessions.State!.Tick;await sessions.Send("step");Check(sessions.State!.Tick==externalTick+1,"External GL update differs");await sessions.StopAsync();
                checks.Add("external-gl-same-snapshot-and-fixed-step");
                Check(original.SequenceEqual(File.ReadAllBytes("samples/motion-lab/game.start.json")),"Backend switching saved source content");
                documents.Set(ModuleDocument.Open("samples/quick-cards"));Backend("render.desktopgl");await views.Apply();
                var board=app.Service<BoardSessionController>("board.sessions");await board.Start();Check(board.Active,board.Status);
                var table=views.CreateBoard();table.Show(BoardGame.View.TableScene.From(board.Frame!.View,board.Module!.Resources));
                preview=new Window{Title="Board GL preview",Width=1050,Height=700,Content=table};preview.Show();await Drawn(table.Surface);table.SaveImage(Path.Combine(output,"board-gl.png"));
                var revision=board.Frame.Revision;Backend("render.windowsdx");await views.Apply();await Drawn(table.Surface,table.RenderedFrames);
                Check(board.Frame.Revision==revision && board.Active,"Backend switch restarted board session");table.Dispose();preview.Content=null;await table.Shutdown;preview.Close();preview=null;board.Stop();
                checks.Add("board-dx-gl-render-binding-keeps-session-and-revision");
                Backend("render.desktopgl");board.External=true;board.Executable=player;await board.Start();Check(board.Active,board.Status);board.Stop();
                checks.Add("external-board-gl-handshake-renders-before-ready-and-closes");
                var rebuilding=views.Apply();documents.Set(ModuleDocument.Open("samples/motion-lab"));await rebuilding;
                Check(!views.Busy && views.Status.Contains("失败") && !documents.Dirty,"Document switch committed a stale graphics operation");
                checks.Add("document-switch-cancels-rebuild-without-changing-new-project");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                one?.Dispose();two?.Dispose();preview?.Close();
                try{app.Service<DocumentWorkspace>("documents").Set(null);await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors,dxDevices=WindowsDxBackend.LiveDevices,dxBackends=WindowsDxBackend.LiveBackends}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
