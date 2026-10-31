using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using BoardGame.Authoring;
using BoardGame.Plugins;

namespace BoardGame.Editor;

internal static class NativeExportVerification
{
    public static Window Create(string sdk,string fixture,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);var host=new EditorHost("samples/motion-lab");
        host.Window.Opened+=async(_,_)=>
        {
            var checks=new List<string>();var errors=new List<string>();var app=host.Application;
            void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
            void Trace(string message)=>File.WriteAllText(Path.Combine(output,"progress.txt"),message);
            try
            {
                await host.Ready;var documents=app.Service<DocumentWorkspace>("documents");var workflow=app.Service<ExportWorkflow>("exports");var commands=app.Service<EditorCommands>("commands");
                var original=ProjectPaths.Read(documents.Current!.DirectoryPath!);var source=ProjectView.Version(original);
                Check(workflow.Providers.Items.Count==3 && commands.Items.Any(p=>p.Id=="native.export"),"Native provider is not discoverable");
                workflow.Selected="native-directory";workflow.SetParameter(workflow.Selected,"sdk",sdk);workflow.Destination=Path.Combine(output,"game");
                var startup=JsonNode.Parse(documents.Draft("game.start.json").Text)!.AsObject();
                Check(!await workflow.Run(false) && workflow.Progress.Contains("exactly one render.backend"),"Missing backend accepted: "+workflow.Progress);
                startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="render.desktopgl",["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
                documents.EditDraft("game.start.json",startup.ToJsonString());
                workflow.SetParameter(workflow.Selected,"dotnet",Path.Combine(output,"missing-dotnet.exe"));
                Check(!await workflow.Run(false) && workflow.Progress.Contains("toolchain.sdk"),"Missing toolchain not diagnosed");workflow.SetParameter(workflow.Selected,"dotnet","");
                var duplicate=startup.DeepClone();duplicate["plugins"]!.AsArray().Add(new JsonObject{["id"]="render.windowsdx",["version"]="1",["api"]=1,["configuration"]=new JsonObject()});
                documents.EditDraft("game.start.json",duplicate.ToJsonString());Check(!await workflow.Run(false) && workflow.Progress.Contains("exactly one"),"Multiple backends accepted");
                documents.EditDraft("game.start.json",startup.ToJsonString());
                var store=app.Service<PluginStore>("runtime.plugins");store.Import(Path.Combine(fixture,"counter"));var configuration=app.Service<RuntimeConfiguration>("runtime.configuration");
                configuration.Select(configuration.Available().Single(p=>p.Definition.Id=="sample.counter"));
                Check(!await workflow.Run(false) && workflow.Progress.Contains("aot.undeclared-or-incompatible"),"Undeclared external implementation accepted");documents.Undo();
                Check(await workflow.Run(false),workflow.Progress);Check(workflow.Details.Contains("render.desktopgl") && workflow.Details.Contains("motion.loop") && workflow.Details.Contains("工具链"),"Native closure/toolchain details missing");
                checks.Add("three-providers-backend-toolchain-aot-diagnostics-and-exact-closure");
                await app.Disable("author.export.native");Check(!commands.Items.Any(p=>p.Id=="native.export") && !await workflow.Run(false),"Disabled native provider still executes");
                await app.Enable("author.export.native");Check(workflow.Parameter("native-directory","sdk")==sdk && commands.Items.Count(p=>p.Id=="native.export")==1,"Native activation lost settings");
                await app.Disable("author.export.package");await app.Disable("author.export.player");Check(workflow.Providers.Items.Count==1,"Native provider depends on other exports");
                workflow.Destination=Path.Combine(output,"cancelled");var cancelAtCompiler=false;
                void CancelCompile(){if(!cancelAtCompiler && workflow.Progress.Contains("Generating native code")){cancelAtCompiler=true;workflow.Cancel();}}
                workflow.Changed+=CancelCompile;Trace("cancel real native compiler");
                var cancelled=workflow.Run(true);
                try{await app.Disable("author.export.native");throw new InvalidDataException("Active native provider unloaded");}catch(InvalidOperationException error){Check(error.Message.Contains("执行"),error.Message);}
                Check(!await cancelled && cancelAtCompiler && !Directory.Exists(workflow.Destination),"Native cancellation left a successful directory");workflow.Changed-=CancelCompile;
                checks.Add("native-compiler-cancellation-and-active-provider-protection");
                var motion=JsonNode.Parse(documents.Draft("motion.json").Text)!.AsObject();motion["title"]="Editor 未保存原生游戏";documents.EditDraft("motion.json",motion.ToJsonString());
                workflow.SetParameter("native-directory","title","Editor 未保存原生游戏");workflow.Destination=Path.Combine(output,"game");
                var expected=documents.PreviewAuthorView(new(documents.Current.DirectoryPath!,"editor.export","gui",documents.WorkspaceId,documents.Generation));
                Trace("publish unsaved native snapshot");await commands.Execute("native.export");
                Check(File.Exists(Path.Combine(workflow.Destination,"Play.exe")),workflow.Progress);
                Check(workflow.Source?.Version==expected.Source.Version,"Native result does not match the captured draft");
                Check(ProjectView.Version(ProjectPaths.Read(documents.Current.DirectoryPath!))==source,"Native publication saved the source");
                Check(File.ReadAllText(Path.Combine(workflow.Destination,"content/motion.json")).Contains("Editor"),"Native output ignored unsaved title");
                checks.Add("public-native-command-publishes-exact-unsaved-snapshot-without-saving");
                SessionVerification.Select(app,"author.export");await Task.Delay(200);
                using(var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)host.Window.Bounds.Width,(int)host.Window.Bounds.Height),new(96,96)))
                {bitmap.Render(host.Window);bitmap.Save(Path.Combine(output,"native-export.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
                File.WriteAllText(Path.Combine(output,"native-details.txt"),workflow.Details+"\n"+workflow.Progress);
                await app.Disable("author.export.native");await app.Enable("author.export.package");workflow.Selected="game-package";workflow.Destination=Path.Combine(output,"draft.bgm");
                Check(await workflow.Run(true),workflow.Progress);Check(File.Exists(workflow.Destination),"Ordinary package unavailable after native disable");
                checks.Add("ordinary-package-works-with-native-provider-disabled");
                await app.Enable("author.export.native");workflow.Selected="native-directory";workflow.Destination=Path.Combine(output,"switched");var switched=false;
                void SwitchDocument(){if(!switched && workflow.Progress.StartsWith("build: Publishing",StringComparison.Ordinal)){switched=true;documents.Set(ModuleDocument.Open("samples/quick-cards"));}}
                workflow.Changed+=SwitchDocument;Trace("cancel on document replacement");Check(!await workflow.Run(true),"Old document export succeeded after replacement");workflow.Changed-=SwitchDocument;
                await Task.Delay(150);Check(switched && workflow.Source==null && workflow.Progress.Length==0 && workflow.Details.Length==0 && !Directory.Exists(Path.Combine(output,"switched")),"Late native result changed new document");
                checks.Add("document-switch-cancels-real-build-and-ignores-late-notifications");
                documents.Set(ModuleDocument.Open("samples/motion-lab"));documents.EditDraft("game.start.json",startup.ToJsonString());workflow.Selected="native-directory";workflow.SetParameter(workflow.Selected,"sdk",sdk);workflow.Destination=Path.Combine(output,"closed");
                documents.Location=Path.Combine(output,"close-fixture");documents.Save();Check(!documents.Dirty,"Close fixture not clean");
                var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void BuildStarted(){if(workflow.Progress.StartsWith("build: Publishing",StringComparison.Ordinal))reached.TrySetResult();}
                workflow.Changed+=BuildStarted;Trace("close cancels and awaits native build");var closing=workflow.Run(true);await reached.Task.WaitAsync(TimeSpan.FromMinutes(2));
                Check(await app.RequestShutdown(),"Clean window shutdown was rejected");Check(!await closing && !Directory.Exists(Path.Combine(output,"closed")),"Shutdown left a native build or output");
                checks.Add("normal-window-shutdown-cancels-and-awaits-real-native-build");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                try{app.Root.Get<DocumentWorkspace>("documents")?.Set(null);await app.DisposeAsync();}catch(Exception error){errors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors}));host.Window.Close();
            }
        };
        return host.Window;
    }
}
