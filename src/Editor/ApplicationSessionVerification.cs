using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using BoardGame.Rendering;

namespace BoardGame.Editor;

internal static class ApplicationSessionVerification
{
    public static Window Create(string output)
    {
        var host=new EditorHost("samples/motion-lab");
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();var app=host.Application;
            async Task Until(Func<bool> ready,string message){for(var i=0;i<300;i++){if(ready())return;await Task.Delay(30);}throw new Exception(message);}
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            try
            {
                await host.Ready;var docs=app.Service<DocumentWorkspace>("documents");var sessions=app.Service<ApplicationSessions>("sessions");
                sessions.Executable=Path.GetFullPath("src/Player/bin/Debug/net10.0-windows/Player.exe");
                var original=File.ReadAllBytes("samples/motion-lab/motion.json");
                var draft=JsonNode.Parse(docs.Current!.Text("motion.json"))!.AsObject();draft["title"]="Unsaved application preview";
                docs.EditDraft("motion.json",draft.ToJsonString());
                foreach(var external in new[]{false,true})
                {
                    sessions.External=external;await sessions.Start();
                    Check(sessions.Active,sessions.Status);await sessions.Send("pause");
                    var tick=sessions.State!.Tick;await sessions.Send("step");
                    Check(sessions.State!.Paused && sessions.State.Tick==tick+1,"Application single step differs");
                    Check(sessions.SourceView!.Text("motion.json").Contains("Unsaved application preview"),"Session lost draft source");
                    Check(original.SequenceEqual(File.ReadAllBytes("samples/motion-lab/motion.json")),"Preview implicitly saved");
                    if(!external)
                    {
                        var instance=sessions.Instance;tick=sessions.State.Tick;
                        for(var cycle=0;cycle<2;cycle++)
                        {
                            await app.Reload("play.application");SessionVerification.Select(app,"play.application");
                            var panel=(FormatPanel)((TabItem)app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem!).Content!;await panel.Ready;
                            var view=panel.GetLogicalDescendants().OfType<ApplicationViewport>().Single();
                            await Until(()=>view.Frames>2 || view.Error!=null,"Application viewport failed to render");Check(view.Error==null,view.Error ?? "");
                            Check(ReferenceEquals(instance,sessions.Instance) && sessions.State!.Tick==tick,"Panel rebuilt simulation");
                        }
                        Check(instance!.Service<IApplicationView>(ApplicationViewPlugin.Capability).Description.Title=="Unsaved application preview","Runtime ignored draft title");
                    }
                    await sessions.Send("resume");await Until(()=>sessions.State!.Tick>tick,"Application did not resume");
                    sessions.Stop();await Until(()=>!sessions.Active,"Application did not stop");
                }
                checks.Add("embedded-external-draft-pause-fixed-step-panel-rebuild-no-save");
                sessions.External=true;await sessions.Start();Check(sessions.Active,sessions.Status);
                using(var child=System.Diagnostics.Process.GetProcessById(sessions.ExternalProcessId!.Value)){child.Kill(true);await child.WaitForExitAsync();}
                await Until(()=>!sessions.Active,"Failed external Player retained session");Check(sessions.Status.Contains("故障"),sessions.Status);
                checks.Add("external-process-failure-removes-stale-state");
                sessions.External=false;
                var starting=sessions.Start();docs.Set(ModuleDocument.Open("samples/quick-cards"));await starting;
                Check(!sessions.Active && sessions.Instance==null && sessions.State==null,"Late application populated another document");
                var board=app.Service<BoardSessionController>("board.sessions");await board.Start();Check(board.Active,board.Status);board.Stop();
                docs.Set(ModuleDocument.Open("samples/motion-lab"));await sessions.Start();Check(sessions.Active,sessions.Status);
                await app.Disable("runtime.motion");Check(!sessions.Active,"Removing runtime provider retained instance");
                await app.Enable("runtime.motion");await sessions.Start();Check(sessions.Active,sessions.Status);sessions.Stop();
                checks.Add("document-switch-late-start-board-session-and-provider-reenable");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                try{await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output,JsonSerializer.Serialize(new{success=errors.Count==0,errors,checks,devices=WindowsDxBackend.LiveDevices,backends=WindowsDxBackend.LiveBackends}));
                host.Window.Close();
            }
        };
        return host.Window;
    }
}
