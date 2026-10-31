using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using BoardGame.Authoring;
using BoardGame.Plugins;

namespace BoardGame.Editor;

internal static class BoardNativeAuthorVerification
{
    public static Window Create(string source,string sdk,string release,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);var host=new EditorHost(source);
        host.Window.Opened+=async(_,_)=>
        {
            var errors=new List<string>();var checks=new List<string>();
            try
            {
                await host.Ready;var app=host.Application;var documents=app.Service<DocumentWorkspace>("documents");
                var original=ProjectView.Version(ProjectPaths.Read(Path.GetFullPath(source)));
                var store=app.Service<PluginStore>("runtime.plugins");store.Import(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!,"cpu-package"));
                var state=JsonNode.Parse(documents.Draft("state.json").Text)!;state["state"]!["entities"]!["counter"]!["faces"]=new JsonArray("中文计数");
                var before=documents.Draft("state.json").Text;documents.EditDraft("state.json",state.ToJsonString());
                documents.Undo();Check(documents.Draft("state.json").Text==before,"Undo changed the wrong file");documents.Redo();
                var commands=app.Service<EditorCommands>("commands");await commands.Execute("board.application.play");
                var preview=app.Service<BoardApplicationPreview>("board.application.preview");
                Check(preview.Active && preview.SourceView!=null,preview.Status);
                Check(JsonNode.Parse(preview.SourceView!.Text("state.json"))!["state"]!["entities"]!["counter"]!["faces"]![0]!.GetValue<string>()=="中文计数","Preview ignored draft");
                for(var wait=0;preview.Frames<3 && wait<100;wait++)await Task.Delay(30);
                Check(preview.Frames>=3 && preview.Error==null,preview.Error ?? "No preview frames");
                await commands.Execute("board.application.stop");Check(!preview.Active,"Preview window still active after stop");
                await commands.Execute("board.application.play");Check(preview.Active,preview.Status);
                await app.Disable("play.board.application");Check(!preview.Active && !commands.Items.Any(p=>p.Id=="board.application.play"),"Preview plugin did not await window shutdown");
                await app.Enable("play.board.application");checks.Add("unsaved-board-preview-exact-source-stop-and-plugin-shutdown");
                var workflow=app.Service<ExportWorkflow>("exports");workflow.Selected="native-directory";
                workflow.SetParameter(workflow.Selected,"sdk",sdk);workflow.SetParameter(workflow.Selected,"host","board.desktop");workflow.Destination=Path.Combine(output,"native");
                Check(await workflow.Run(false),workflow.Progress);Check(workflow.Details.Contains("sample.native-cpu") && workflow.Details.Contains("wasm"),"Selected external/WASM identity not visible");
                checks.Add("editor-discovers-board-application-and-exact-external-wasm-native-closure");
                Check(await workflow.Run(true),workflow.Progress);Check(File.Exists(Path.Combine(output,"native/Play.exe")),"Missing native artifact");
                checks.Add("editor-native-publishes-unsaved-board-state");
                workflow.Selected="player-directory";workflow.SetParameter(workflow.Selected,"player",release);workflow.Destination=Path.Combine(output,"ordinary");
                Check(await workflow.Run(true),workflow.Progress);checks.Add("same-draft-ordinary-player-with-exact-external-plugin");
                workflow.Selected="game-package";workflow.Destination=Path.Combine(output,"board.bgm");Check(await workflow.Run(true),workflow.Progress);
                Check(ProjectView.Version(ProjectPaths.Read(Path.GetFullPath(source)))==original,"Export saved source");
                var draft=documents.PreviewAuthorView(new(documents.Current!.DirectoryPath!,"verify","gui",documents.WorkspaceId,documents.Generation));
                documents.Location=Path.Combine(output,"saved-author");documents.Save();documents.Set(ModuleDocument.Open(documents.Location));
                var reopened=documents.PreviewAuthorView(new(documents.Current!.DirectoryPath!,"verify","gui",documents.WorkspaceId,documents.Generation));
                Check(draft.Source.Version==reopened.Source.Version,"Save/reopen changed draft identity");
                checks.Add("package-export-undo-redo-save-reopen-and-original-source-preserved");
            }
            catch(Exception error){errors.Add(error.ToString());}
            finally
            {
                host.Application.Root.Get<DocumentWorkspace>("documents")?.Set(null);await host.Application.DisposeAsync();
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{success=errors.Count==0,checks,errors}));host.Window.Close();
            }
        };
        return host.Window;
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
}
