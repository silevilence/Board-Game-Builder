using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using BoardGame.Plugins;
using BoardGame.Rendering;

namespace BoardGame.Editor;

internal static class ExternalRuntimeVerification
{
    public static Window Create(string fixture,string executable,string output)
    {
        var host=new EditorHost(Path.Combine(fixture,"project"));
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();var app=host.Application;
            void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
            async Task Until(Func<bool> ready,string message){for(var i=0;i<300;i++){if(ready())return;await Task.Delay(25);}throw new TimeoutException(message);}
            try
            {
                await host.Ready;
                var store=app.Service<PluginStore>("runtime.plugins");
                foreach(var package in new[]{"counter","definition","view"})store.Import(Path.Combine(fixture,package));
                var documents=app.Service<DocumentWorkspace>("documents");var sessions=app.Service<ApplicationSessions>("sessions");
                sessions.Executable=Path.GetFullPath(executable);
                var original=File.ReadAllBytes(Path.Combine(fixture,"project","state.json"));
                documents.EditDraft("state.json","{\"value\":37}");
                foreach(var external in new[]{false,true})
                {
                    sessions.External=external;await sessions.Start();Check(sessions.Active,sessions.Status);
                    await sessions.Send("pause");var tick=sessions.State!.Tick;await sessions.Send("step");
                    Check(sessions.State.Tick==tick+1 && sessions.State.Paused,"Imported fixed update/debug differs");
                    Check(sessions.SourceView!.Text("state.json").Contains("37"),"Imported preview lost the draft");
                    try{store.Remove("sample.counter","1");throw new Exception("Active imported package removed");}catch(InvalidOperationException){}
                    if(!external)
                    {
                        SessionVerification.Select(app,"play.application");
                        var panel=(FormatPanel)((TabItem)app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem!).Content!;await panel.Ready;
                        var viewport=panel.GetLogicalDescendants().OfType<ApplicationViewport>().Single();
                        await Until(()=>viewport.Frames>2 || viewport.Error!=null,"Imported application did not render");Check(viewport.Error==null,viewport.Error ?? "");
                        Check(sessions.Instance!.Service<IApplicationView>(ApplicationViewPlugin.Capability).Description.Title=="External runtime sample","External view not selected");
                    }
                    sessions.Stop();
                }
                Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture,"project","state.json"))),"Preview saved the source");
                checks.Add("embedded-and-external-approved-identity-draft-update-render-lifetime-no-save");
                sessions.External=false;var pending=sessions.Start();documents.Set(ModuleDocument.Open("samples/motion-lab"));await pending;
                Check(!sessions.Active,"Late external definition populated another document");
                await app.Disable("runtime.external");await app.Enable("runtime.external");
                documents.Set(ModuleDocument.Open(Path.Combine(fixture,"project")));await sessions.Start();Check(sessions.Active,sessions.Status);sessions.Stop();
                checks.Add("late-start-provider-reenable");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                try{await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                File.WriteAllText(output,JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors,devices=WindowsDxBackend.LiveDevices,backends=WindowsDxBackend.LiveBackends}));
                host.Window.Close();
            }
        };
        return host.Window;
    }
}
