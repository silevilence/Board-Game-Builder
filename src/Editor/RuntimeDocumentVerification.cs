using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Authoring;
using System.Text.Json.Nodes;

namespace BoardGame.Editor;

internal static class RuntimeDocumentVerification
{
    public static Window Create(string output)
    {
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            void Trace(string message)=>File.WriteAllText(output+".progress",message);
            Trace("opened");
            string? error=null;
            try
            {
                await host.Ready;await host.Application.Enable("author.runtime.board");await host.Application.Enable("author.module");Trace("host ready");var app=host.Application;var documents=app.Service<DocumentWorkspace>("documents");
                foreach(var id in new[]{"author.configuration","author.files","author.startup","author.scripts","author.media","author.turns","author.print"}) {Trace("enable "+id);await app.Enable(id);}
                Control Panel(string id)=>(Control)app.Service<WorkspaceShell>("workspace").Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,id)).Content!;
                async Task WaitFor<T>()
                {
                    for(var i=0;i<100;i++) {if(((FormatPanel)Panel("author.configuration")).Content is T) return;await Task.Delay(20);}
                    throw new Exception("Format panel did not become "+typeof(T).Name);
                }
                await WaitFor<ConfigurationPanel>();
                Trace("first panels ready");
                var definition=documents.Current!.DefinitionPath;
                Trace("edit draft");
                documents.EditDraft(definition,documents.Draft(definition).Text+"\n");
                Trace("reload configuration");
                await app.Reload("author.configuration").WaitAsync(TimeSpan.FromSeconds(20));Trace("wait configuration");await WaitFor<ConfigurationPanel>();
                if(!documents.Draft(definition).Dirty) throw new Exception("Draft lost during format panel rebuild");
                documents.DiscardDraft(definition);
                // A real structured non-board format must not acquire board editors.
                using(var format=documents.AuthorFormats.Register(new("motion.definition","1",1,p=>new MotionFormat(p.Configuration.GetProperty("path").GetString()!))))
                {
                    documents.Set(ModuleDocument.Open("samples/motion-lab"));
                    await ((FormatPanel)Panel("author.configuration")).Ready;
                    foreach(var id in new[]{"author.media","author.turns"})
                    {
                        var wrapper=(FormatPanel)Panel(id);await wrapper.Ready;
                        if(wrapper.Content is not TextBlock) throw new Exception("Non-board format mounted "+id);
                    }
                    if(!documents.Current.SupportsStructuredEditing || documents.Current.SupportsAuthorCapability(GameAuthorFormats.BoardModel)) throw new Exception("Author model capabilities confused");
                    if(((FormatPanel)Panel("author.configuration")).Content is not ConfigurationPanel) throw new Exception("Generic configuration unavailable");
                }
                documents.Set(ModuleDocument.Create());await WaitFor<ConfigurationPanel>();
                EditorPluginScope? captured=null;var notifications=0;
                app.Install(new("probe.document-child",1,[],["documents","panels"],scope=>
                    scope.Service<PanelContributions>("panels").Register(scope,"probe.document-child","文档代次探针",()=>new FormatPanel(scope,child=>
                    {captured=child;child.OnDocumentChanged(()=>notifications++);return new TextBlock();}))));
                await app.Enable("probe.document-child");await ((FormatPanel)Panel("probe.document-child")).Ready;
                var oldScope=captured!;var oldNotifications=notifications;
                documents.Set(ModuleDocument.Create());
                if(oldScope.Document!=null || notifications!=oldNotifications) throw new Exception("Old child observed new document before disposal");
                await ((FormatPanel)Panel("probe.document-child")).Ready;
                if(ReferenceEquals(oldScope,captured)) throw new Exception("Same-format document reused its old child");
                await app.Disable("probe.document-child");
                for(var i=0;i<3;i++)
                {
                    Trace("cycle "+i+" switch alternate");
                    documents.Set(ModuleDocument.Open("samples/state-minimal"));await WaitFor<TextBlock>();
                    if(documents.Current!.SupportsStructuredEditing) throw new Exception("Unsupported format advertised structured editing");
                    documents.EditDraft("position.json",documents.Draft("position.json").Text+"\n");
                    await app.Reload("author.files");
                    if(!documents.Draft("position.json").Dirty) throw new Exception("Generic draft lost");
                    documents.ApplyDrafts();_=documents.Current.Snapshot();
                    var sessions=app.Service<BoardSessionController>("board.sessions");await sessions.Start();
                    Trace("cycle "+i+" session started");
                    if(!sessions.Active || sessions.Frame==null) throw new Exception("Alternate format did not start");
                    var revision=sessions.Frame.Revision;
                    await app.Reload("author.configuration");await WaitFor<TextBlock>();
                    if(!sessions.Active || sessions.Frame.Revision!=revision) throw new Exception("Panel rebuilt the live session");
                    sessions.Stop();documents.Set(ModuleDocument.Create());await WaitFor<ConfigurationPanel>();
                    documents.Set(ModuleDocument.Open("samples/state-minimal"));
                    await app.Disable("author.configuration");await app.Disable("author.print");
                    await app.Enable("author.configuration");await app.Enable("author.print");await WaitFor<TextBlock>();
                    documents.Set(ModuleDocument.Create());await WaitFor<ConfigurationPanel>();
                }
                Trace("dispose");documents.Set(null);await app.DisposeAsync();
                if(app.Root.Fiber.DisposableCount!=0) throw new Exception("Format child fibers leaked effects");
            }
            catch(Exception failure) {error=failure.ToString();}
            finally
            {
                try
                {
                    host.Application.Root.Get<DocumentWorkspace>("documents")?.Set(null);
                    await host.Application.DisposeAsync();
                }
                catch(Exception cleanup) {error=error==null?cleanup.ToString():error+"\nCleanup: "+cleanup;}
                File.WriteAllText(output,Wire.Json(new {success=error==null,error}));
                host.Window.Close();
            }
        };
        return host.Window;
    }
    private sealed record MotionFormat(string DefinitionPath):IGameAuthorFormat
    {
        public JsonObject Read(IAuthorFiles files)=>JsonNode.Parse(files.Text(DefinitionPath))!.AsObject();
        public string Serialize(IAuthorFiles files,JsonObject configuration)=>configuration.ToJsonString();
    }
}
