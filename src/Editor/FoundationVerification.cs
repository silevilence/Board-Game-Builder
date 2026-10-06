using Avalonia.Controls;
using BoardGame.Kernel;

namespace BoardGame.Editor;

internal static class FoundationVerification
{
    public static Window Create(string output)
    {
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            var errors=new List<string>();
            try
            {
                await host.Ready;
                var app=host.Application;
                var document=app.Service<DocumentWorkspace>("documents").Current ?? throw new Exception("No empty document");
                if(document.Snapshot().Id!="untitled") throw new Exception("Wrong empty document");
                app.Install(new("probe.optional",1,[],["documents"],_=>throw new Exception("optional probe failure")));
                try {await app.Enable("probe.optional");throw new Exception("Missing failure");}
                catch(Exception e) when(e.Message=="optional probe failure") { }
                if(!ReferenceEquals(document,app.Service<DocumentWorkspace>("documents").Current)) throw new Exception("Optional failure changed document");
                var panels=app.Service<PanelContributions>("panels");
                var baseline=panels.Count;var released=0;
                app.Install(new("probe.panel",1,[],["panels"],scope=>
                {
                    scope.Own(new Cleanup(()=>released++));
                    scope.Service<PanelContributions>("panels").Register(scope,"probe.panel","示例面板",()=>new TextBlock {Text="Independent panel"});
                }));
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Enable("probe.panel");
                    if(panels.Count!=baseline+1) throw new Exception("Duplicate or missing panel");
                    await app.Disable("probe.panel");
                    if(panels.Count!=baseline || released!=cycle+1) throw new Exception("Panel effects leaked");
                }
                app.Install(new("probe.factory",1,[],["panels"],scope=>
                {
                    panels.Register(scope,"probe.temporary","temporary",()=>new TextBlock());
                    panels.Register(scope,"probe.throwing","throwing",()=>throw new Exception("factory failed"));
                }));
                try {await app.Enable("probe.factory");throw new Exception("Factory failure ignored");}
                catch(Exception e) when(e.Message=="factory failed") { }
                if(panels.Count!=baseline) throw new Exception("Factory failure leaked registration");
                app.Install(new("probe.duplicate",1,[],["panels"],scope=>panels.Register(scope,"base.settings","duplicate",()=>new TextBlock())));
                try {await app.Enable("probe.duplicate");throw new Exception("Duplicate accepted");}
                catch(InvalidDataException e) when(e.Message.Contains("Duplicate panel")) { }
                if(panels.Count!=baseline) throw new Exception("Unrelated panel removed");
                var commands=app.Service<EditorCommands>("commands");var commandBaseline=commands.Items.Count;var executions=0;var enabled=true;
                app.Install(new("probe.command",1,[],["commands"],scope=>commands.Register(scope,new("probe.command","验证命令",()=> {executions++;return Task.CompletedTask;},()=>enabled,"Ctrl+F8"))));
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Enable("probe.command");
                    var button=app.Service<WorkspaceShell>("workspace").Toolbar.Children.OfType<Button>().Single(b=>Equals(b.Content,"验证命令"));
                    button.Command!.Execute(null);
                    host.Window.RaiseEvent(new Avalonia.Input.KeyEventArgs {RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.F8,KeyModifiers=Avalonia.Input.KeyModifiers.Control});
                    if(executions!=(cycle+1)*2) throw new Exception("Button/shortcut differ or duplicate");
                    enabled=false;commands.Refresh();
                    if(button.Command.CanExecute(null)) throw new Exception("Command enabled state differs");
                    await commands.Execute("probe.command");
                    if(executions!=(cycle+1)*2) throw new Exception("Disabled command ran");
                    enabled=true;await app.Disable("probe.command");
                    if(commands.Items.Count!=commandBaseline) throw new Exception("Command registration survived");
                }
                var documents=app.Service<DocumentWorkspace>("documents");
                foreach(var id in new[]{"author.resources","author.yaml","author.prototypes","author.media","author.turns"})
                {await app.Enable(id);await app.Reload(id);}
                Authoring.CreatePrototype(document,"probe");Authoring.Instantiate(document,"probe","probe-instance");
                Authoring.SetComponent(document,"prototypes","probe","properties",new() {["value"]=3});
                documents.Select("entities","probe-instance");
                await app.Reload("author.media");await app.Reload("author.prototypes");
                if(documents.Selection!=("entities","probe-instance")) throw new Exception("Shared selection lost");
                _=document.Snapshot();
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Enable("author.layout");SessionVerification.Select(app,"author.layout");
                    host.Window.Width+=30;host.Window.Height+=20;await Task.Delay(250);
                    var layout=(LayoutPanel)((TabItem)app.Service<WorkspaceShell>("workspace").Tabs.SelectedItem!).Content!;
                    layout.Table.ChangeZoom(1.1f);layout.Table.Hint("probe-instance",null);layout.Table.CancelInput();
                    if(layout.Table.Selected!=null || layout.Table.RenderedFrames==0) throw new Exception("Layout input cancellation/render failed");
                    SessionVerification.Select(app,"base.settings");await app.Disable("author.layout");await Task.Delay(50);
                    if(BoardGame.View.DesktopViewport.LiveDevices!=0 || BoardGame.View.DesktopViewport.LiveNativeWindows!=0) throw new Exception("Viewport/native window leaked");
                }
                await app.Enable("author.configuration");
                var original=documents.Draft("module.json").Text;
                documents.EditDraft("module.json",original+"\n ");
                await app.Reload("author.configuration");
                var config=(ConfigurationPanel)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.configuration")).Content!;
                if(config.Source.Text!=original+"\n ") throw new Exception("Draft was lost with panel");
                documents.Undo();if(documents.Draft("module.json").Text!=original) throw new Exception("Draft undo lost");
                documents.Redo();if(documents.Draft("module.json").Text==original) throw new Exception("Draft redo lost");
                if(await documents.CanLeave(()=>Task.FromResult(UnsavedChoice.Cancel))) throw new Exception("Cancel left document");
                if(!documents.Draft("module.json").Dirty) throw new Exception("Cancel lost draft");
                document.EditConfiguration(c=>c["version"]="conflict");
                try {documents.ApplyDrafts();throw new Exception("Conflict ignored");}
                catch(InvalidOperationException e) when(e.Message.Contains("草稿")) { }
                documents.DiscardDraft("module.json");document.Undo();
                document.EditText("rules.lua",document.Text("rules.lua")+"\n-- retained across panel reconstruction\n");
                await app.Reload("author.configuration");documents.Undo();documents.Redo();
                if(!document.Text("rules.lua").Contains("retained across")) throw new Exception("Document undo history lost");
                documents.Location=Path.GetFullPath(output)+"-author-"+Guid.NewGuid();
                if(!await documents.CanLeave(()=>Task.FromResult(UnsavedChoice.Save))) throw new Exception("Save cancelled");
                if(ModuleDocument.Open(documents.Location).Snapshot().Fingerprint!=document.Snapshot().Fingerprint) throw new Exception("Save/reopen differs");
                try {await app.Disable("base.documents");throw new Exception("Required plugin was disabled");}
                catch(InvalidOperationException e) when(e.Message.Contains("必需")) { }
                await app.DisposeAsync();
                if(app.Root.Fiber.DisposableCount!=0) throw new Exception("Root effects leaked");
            }
            catch(Exception error) {errors.Add(error.ToString());}
            finally {File.WriteAllText(Path.GetFullPath(output),Wire.Json(new {success=errors.Count==0,errors}));host.Window.Close();}
        };
        return host.Window;
    }
}
