using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
                async Task ReadyPanels()
                {
                    foreach(var panel in app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Select(t=>t.Content).OfType<FormatPanel>()) await panel.Ready;
                }
                async Task Enable(string id) {await app.Enable(id);await ReadyPanels();}
                async Task Reload(string id) {await app.Reload(id);await ReadyPanels();}
                await Task.Delay(100);
                var settings=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"base.settings"));
                if(!settings.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="设置")) throw new Exception("Panel title is not rendered");
                var pluginPanel=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"base.plugins"));
                if(!((Control)pluginPanel.Content!).GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="加载程序集")) throw new Exception("Panel button label is not rendered");
                var document=app.Service<DocumentWorkspace>("documents").Current ?? throw new Exception("No empty document");
                if(document.Snapshot().Id!="untitled") throw new Exception("Wrong empty document");
                app.Install(new("probe.optional",1,[],["documents"],_=>throw new Exception("optional probe failure")));
                try {await Enable("probe.optional");throw new Exception("Missing failure");}
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
                    await Enable("probe.panel");
                    if(panels.Count!=baseline+1) throw new Exception("Duplicate or missing panel");
                    await app.Disable("probe.panel");
                    if(panels.Count!=baseline || released!=cycle+1) throw new Exception("Panel effects leaked");
                }
                app.Install(new("probe.factory",1,[],["panels"],scope=>
                {
                    panels.Register(scope,"probe.temporary","temporary",()=>new TextBlock());
                    panels.Register(scope,"probe.throwing","throwing",()=>throw new Exception("factory failed"));
                }));
                try {await Enable("probe.factory");throw new Exception("Factory failure ignored");}
                catch(Exception e) when(e.Message=="factory failed") { }
                if(panels.Count!=baseline) throw new Exception("Factory failure leaked registration");
                app.Install(new("probe.duplicate",1,[],["panels"],scope=>panels.Register(scope,"base.settings","duplicate",()=>new TextBlock())));
                try {await Enable("probe.duplicate");throw new Exception("Duplicate accepted");}
                catch(InvalidDataException e) when(e.Message.Contains("Duplicate panel")) { }
                if(panels.Count!=baseline) throw new Exception("Unrelated panel removed");
                var commands=app.Service<EditorCommands>("commands");var commandBaseline=commands.Items.Count;var executions=0;var enabled=true;
                app.Install(new("probe.command",1,[],["commands"],scope=>commands.Register(scope,new("probe.command","验证命令",()=> {executions++;return Task.CompletedTask;},()=>enabled,"Ctrl+F8"))));
                for(var cycle=0;cycle<3;cycle++)
                {
                    await Enable("probe.command");
                    if(cycle==0)
                    {
                        app.Install(new("probe.alias",1,[],["commands"],scope=>commands.Register(scope,new("probe.alias","冲突别名",()=>Task.CompletedTask,()=>true,"Control+F8"))));
                        try {await Enable("probe.alias");throw new Exception("Shortcut alias conflict ignored");}
                        catch(InvalidDataException e) when(e.Message.Contains("Shortcut conflict")) { }
                    }
                    var button=app.Service<WorkspaceShell>("workspace").Toolbar.Children.OfType<Button>().Single(b=>Equals(b.Content,"验证命令"));
                    button.Command!.Execute(null);
                    host.Window.RaiseEvent(new Avalonia.Input.KeyEventArgs {RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.F8,KeyModifiers=Avalonia.Input.KeyModifiers.Control});
                    if(executions!=(cycle+1)*2) throw new Exception("Button/shortcut differ or duplicate");
                    app.Service<WorkspaceShell>("workspace").Toolbar.Children.OfType<TextBox>().Single().RaiseEvent(new Avalonia.Input.KeyEventArgs {RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.F8,KeyModifiers=Avalonia.Input.KeyModifiers.Control});
                    if(executions!=(cycle+1)*2) throw new Exception("Author shortcut ran from text editor");
                    enabled=false;commands.Refresh();
                    if(button.Command.CanExecute(null)) throw new Exception("Command enabled state differs");
                    await commands.Execute("probe.command");
                    if(executions!=(cycle+1)*2) throw new Exception("Disabled command ran");
                    enabled=true;await app.Disable("probe.command");
                    if(commands.Items.Count!=commandBaseline) throw new Exception("Command registration survived");
                }
                var pendingCommand=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                app.Install(new("probe.busy",1,[],["commands"],scope=>commands.Register(scope,new("probe.busy","忙命令",()=>pendingCommand.Task,()=>true))));
                await Enable("probe.busy");var runningCommand=commands.Execute("probe.busy");
                try {await app.Disable("probe.busy");throw new Exception("Running command was unloaded");}
                catch(InvalidOperationException e) when(e.Message.Contains("正在执行")) { }
                finally {pendingCommand.SetResult();await runningCommand;}
                await app.Disable("probe.busy");
                var documents=app.Service<DocumentWorkspace>("documents");
                foreach(var id in new[]{"author.resources","author.yaml","author.prototypes","author.media","author.turns"})
                {await Enable(id);await Reload(id);}
                Authoring.CreatePrototype(document,"probe");Authoring.Instantiate(document,"probe","probe-instance");
                Authoring.SetComponent(document,"prototypes","probe","properties",new() {["value"]=3});
                documents.Select("entities","probe-instance");
                await Reload("author.media");await Reload("author.prototypes");
                if(documents.Selection!=("entities","probe-instance")) throw new Exception("Shared selection lost");
                T Panel<T>(string id) where T:Control {var content=app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,id)).Content;return (T)(content is FormatPanel format?format.Content!:content!);}
                var turnInput=Panel<TurnPanel>("author.turns").GetLogicalDescendants().OfType<TextBox>().First();
                var turnDraft=turnInput.Text+"\n";turnInput.Text=turnDraft;
                await Reload("author.turns");
                if(Panel<TurnPanel>("author.turns").GetLogicalDescendants().OfType<TextBox>().First().Text!=turnDraft) throw new Exception("Seat draft lost with panel");
                documents.Undo();documents.Redo();
                if(Panel<TurnPanel>("author.turns").GetLogicalDescendants().OfType<TextBox>().First().Text!=turnDraft) throw new Exception("Seat draft Undo/Redo failed");
                if(await documents.CanLeave(()=>Task.FromResult(UnsavedChoice.Cancel))) throw new Exception("Field draft Cancel failed");
                try {documents.Save();throw new Exception("Unapplied field draft was silently saved");} catch(InvalidOperationException e) when(e.Message.Contains("字段草稿")) { }
                documents.DiscardForm("席位与阶段");
                var component=Panel<AuthorPanel>("author.prototypes").GetLogicalDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn);
                component.Text="value: 7\n";await Reload("author.prototypes");
                if(Panel<AuthorPanel>("author.prototypes").GetLogicalDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn).Text!="value: 7\n") throw new Exception("Component draft lost");
                document.EditConfiguration(c=>c["phases"]!.AsArray().Add("phase-changed"));
                try {documents.ApplyForm("组件 YAML/entities/probe-instance/properties",doc=>Authoring.SetComponent(doc,"entities","probe-instance","properties",new() {["value"]=7}));throw new Exception("Stale YAML silently overwrote changes");}
                catch(InvalidOperationException e) when(e.Message.Contains("草稿")) { }
                document.Undo();documents.DiscardForm("组件 YAML/entities/probe-instance/properties");
                var mediaInput=Panel<MediaInspector>("author.media").GetLogicalDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn);
                mediaInput.Text="1\n2\ninvalid";await Reload("author.media");
                if(Panel<MediaInspector>("author.media").GetLogicalDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn).Text!="1\n2\ninvalid") throw new Exception("Unparsed media draft lost");
                documents.DiscardForm("媒介字段/entities/probe-instance/dice");
                _=document.Snapshot();
                for(var cycle=0;cycle<3;cycle++)
                {
                    await Enable("author.layout");SessionVerification.Select(app,"author.layout");
                    host.Window.Width+=30;host.Window.Height+=20;await Task.Delay(250);
                    var layout=Panel<LayoutPanel>("author.layout");
                    for(var frame=0;frame<100 && layout.Table.RenderedFrames==0;frame++) await Task.Delay(50);
                    layout.Table.ChangeZoom(1.1f);layout.Table.Hint("probe-instance",null);layout.Table.CancelInput();
                    if(layout.Table.Selected!=null || layout.Table.RenderedFrames==0) throw new Exception($"Layout input cancellation/render failed: frames={layout.Table.RenderedFrames}, bounds={layout.Table.Bounds}, visible={layout.Table.IsVisible}, messages={string.Join(';',layout.GetLogicalDescendants().OfType<TextBlock>().Select(t=>t.Text))}");
                    SessionVerification.Select(app,"base.settings");await app.Disable("author.layout");await Task.Delay(50);
                    if(BoardGame.View.DesktopViewport.LiveDevices!=0 || BoardGame.View.DesktopViewport.LiveNativeWindows!=0) throw new Exception("Viewport/native window leaked");
                }
                await Enable("author.configuration");
                var original=documents.Draft("module.json").Text;
                documents.EditDraft("module.json",original+"\n ");
                await Reload("author.configuration");
                var config=Panel<ConfigurationPanel>("author.configuration");
                if(config.Source.Text!=original+"\n ") throw new Exception("Draft was lost with panel");
                documents.Undo();if(documents.Draft("module.json").Text!=original) throw new Exception("Draft undo lost");
                documents.Redo();if(documents.Draft("module.json").Text==original) throw new Exception("Draft redo lost");
                if(await documents.CanLeave(()=>Task.FromResult(UnsavedChoice.Cancel))) throw new Exception("Cancel left document");
                if(!documents.Draft("module.json").Dirty) throw new Exception("Cancel lost draft");
                document.EditConfiguration(c=>c["phases"]!.AsArray().Add("phase-conflict"));
                try {documents.ApplyDrafts();throw new Exception("Conflict ignored");}
                catch(InvalidOperationException e) when(e.Message.Contains("草稿")) { }
                documents.DiscardDraft("module.json");document.Undo();
                document.EditText("rules.lua",document.Text("rules.lua")+"\n-- retained across panel reconstruction\n");
                await Reload("author.configuration");documents.Undo();documents.Redo();
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
            finally
            {
                try {await host.Application.DisposeAsync();} catch(Exception error) {errors.Add(error.ToString());}
                File.WriteAllText(Path.GetFullPath(output),Wire.Json(new {success=errors.Count==0,errors}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
