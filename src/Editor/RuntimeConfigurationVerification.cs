using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using BoardGame.Authoring;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Editor;

internal static class RuntimeConfigurationVerification
{
    public static Window Create(string fixture,string output)
    {
        Directory.CreateDirectory(output);var host=new EditorHost("samples/motion-lab");
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();var app=host.Application;
            void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
            try
            {
                await host.Ready;var documents=app.Service<DocumentWorkspace>("documents");var configuration=app.Service<RuntimeConfiguration>("runtime.configuration");
                var source=documents.Current!.CopyFiles();
                Check(configuration.Describe().Contains("motion.loop") && !configuration.Startup().Plugins.Any(p=>p.Id=="session"),"Non-board configuration gained board plugins");
                var original=documents.Draft(GameRuntime.Entry).Text;
                documents.EditDraft(GameRuntime.Entry,"{ invalid");await app.Reload("author.startup");configuration=app.Service<RuntimeConfiguration>("runtime.configuration");
                Check(documents.Draft(GameRuntime.Entry).Text=="{ invalid" && configuration.Describe().Contains("诊断"),"Invalid entry was lost during panel reload");
                documents.Undo();Check(documents.Draft(GameRuntime.Entry).Text==original,"Entry draft undo failed");
                var altered=JsonNode.Parse(original)!.AsObject();altered["plugins"]!.AsArray().Add(new JsonObject{["id"]="missing.plugin",["version"]="8",["api"]=1,["configuration"]=new JsonObject{["unknown"]=17}});
                documents.EditDraft(GameRuntime.Entry,altered.ToJsonString());Check(configuration.Describe().Contains("缺少匹配"),"Missing implementation not diagnosed");
                await app.Reload("author.startup");Check(documents.Draft(GameRuntime.Entry).Text.Contains("unknown"),"Unknown configuration discarded");documents.Undo();
                configuration=app.Service<RuntimeConfiguration>("runtime.configuration");
                var external=app.Service<PluginStore>("runtime.plugins");foreach(var name in new[]{"counter","definition","view"})external.Import(Path.Combine(fixture,name));
                var counter=configuration.Available().Single(p=>p.Definition.Id=="sample.counter");configuration.Select(counter);
                Check(configuration.Startup().Plugins.Single(p=>p.Id=="sample.counter").Implementation==counter.Manifest!.Fingerprint,"Selected external version was not pinned");
                documents.Undo();Check(!configuration.Startup().Plugins.Any(p=>p.Id=="sample.counter"),"Enable undo failed");documents.Redo();
                await configuration.Validate();Check(configuration.Validation.Contains("通过"),configuration.Validation);
                var sessions=app.Service<ApplicationSessions>("sessions");await sessions.Start();Check(sessions.Active,sessions.Status);
                var used=sessions.SourceView!.Source.Version;
                configuration.Remove("sample.counter");Check(configuration.Describe().Contains(used[..12]) && sessions.SourceView.Source.Version==used,"Current session was relabeled after editing");
                documents.Undo();sessions.Stop();
                checks.Add("builtin-external-pinned-enable-remove-undo-redo-metadata-and-live-source");
                var workflow=app.Service<ExportWorkflow>("exports");workflow.Selected="game-package";workflow.SetParameter("game-package","target",Path.Combine(output,"saved-config.bgm"));
                workflow.SetParameter("native-directory","sdk","C:/publisher/sdk");workflow.SetParameter("native-directory","future.option","preserved");
                var settings=PublicationEditing.Read(documents).ToJsonString();
                workflow.SetParameter("native-directory","sdk","C:/changed/sdk");documents.Undo();Check(workflow.Parameter("native-directory","sdk")=="C:/publisher/sdk","Publication parameter undo failed");
                documents.Location=Path.Combine(Path.GetFullPath(output),"saved-motion");documents.Save();
                Check(source.All(p=>File.ReadAllBytes(Path.Combine("samples/motion-lab",p.Key)).SequenceEqual(p.Value)),"Source example changed");
                documents.Set(ModuleDocument.Open(documents.Location));
                Check(PublicationEditing.Read(documents).ToJsonString()==settings && workflow.Selected=="game-package","Publication settings lost on reopen");
                Check(configuration.Startup().Plugins.Single(p=>p.Id=="sample.counter").Implementation==counter.Manifest.Fingerprint,"Reopen changed external identity");
                Check(await workflow.Run(true),workflow.Progress);Check(File.Exists(workflow.Destination),"Persisted publication parameters were not consumed");
                await app.Disable("author.export.package");Check(workflow.Selected=="game-package" && workflow.Parameter("native-directory","future.option")=="preserved","Provider removal discarded settings");await app.Enable("author.export.package");
                checks.Add("publication-settings-undo-save-reopen-disabled-provider-preservation-and-export");
                documents.Set(ModuleDocument.Open(Path.Combine(fixture,"project")));Check(!documents.Current!.SupportsStructuredEditing,"External definition incorrectly acquired board author panels");
                await configuration.Validate();Check(configuration.Validation.Contains("通过"),configuration.Validation);
                documents.EditDraft("state.json","{\"value\":99}");documents.Location=Path.Combine(Path.GetFullPath(output),"saved-external");documents.Save();documents.Set(ModuleDocument.Open(documents.Location));
                Check(documents.Current!.Text("state.json").Contains("99") && workflow.Parameter("native-directory","sdk")!="C:/publisher/sdk","External generic editing or document isolation failed");
                documents.Set(ModuleDocument.Open("samples/quick-cards"));Check(configuration.Startup().Plugins.Any(p=>p.Id=="board.render"),"Old explicit board renderer disappeared");
                checks.Add("external-generic-file-edit-save-reopen-and-legacy-document-isolation");
                SessionVerification.Select(app,"author.startup");
                await Task.Delay(200);
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)host.Window.Bounds.Width,(int)host.Window.Bounds.Height),new(96,96));
                bitmap.Render(host.Window);bitmap.Save(Path.Combine(output,"configuration.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                try{app.Service<DocumentWorkspace>("documents").Set(null);await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
