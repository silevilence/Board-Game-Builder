using Avalonia.Controls;
using BoardGame.Kernel;

namespace BoardGame.Editor;

internal static class ExportVerification
{
    public static Window Create(string module,string output,string release)
    {
        var host=new EditorHost(module);
        host.Window.Opened+=async (_,_)=>
        {
            var errors=new List<string>();var passed=new List<string>();Directory.CreateDirectory(output);
            try
            {
                await host.Ready;var app=host.Application;var documents=app.Service<DocumentWorkspace>("documents");
                documents.Location=Path.Combine(Path.GetFullPath(output),"author");
                var sourceDocument=documents.Current!;var original=sourceDocument.CopyFiles();
                var entry=BoardGame.Runtime.GameRuntime.Entry;
                var startup=System.Text.Json.Nodes.JsonNode.Parse(sourceDocument.Text(entry))!;
                startup["version"]=startup["version"]!.GetValue<string>()+".draft";
                documents.EditDraft(entry,startup.ToJsonString());
                var view=documents.AuthorView(new(sourceDocument.DirectoryPath!,"editor.export","gui",documents.WorkspaceId,documents.Generation));
                string expected;
                using(var preview=new BoardGame.Authoring.ProjectSnapshot(view)) expected=BoardGame.Runtime.GameRuntime.Load(preview.Directory).Fingerprint;
                var workflow=app.Service<ExportWorkflow>("exports");workflow.ReleasePath=release;workflow.Destination=Path.Combine(Path.GetFullPath(output),"export");
                FileStream? held=null;string? temporary=null;
                app.Install(new("probe.export-step",1,[],["contributions"],scope=>scope.Service<EditorContributions>("contributions").Register(scope,"export-step","probe",(Action<ExportContext>)(c=>
                {
                    File.WriteAllText(Path.Combine(c.Destination,"extra.txt"),"probe");temporary=c.ModuleDirectory;
                    held=File.Open(Path.Combine(temporary,BoardGame.Runtime.GameRuntime.Entry),FileMode.Open,FileAccess.Read,FileShare.Read);
                }))));
                await app.Enable("probe.export-step");
                var exporting=workflow.Run(true);
                if(exporting.IsCompleted && !await exporting) throw new Exception(workflow.Progress);
                foreach(var id in new[]{"probe.export-step","author.export"})
                {
                    try {await app.Disable(id);throw new Exception("Running export was unloaded");}
                    catch(InvalidOperationException e) when(e.Message.Contains("执行")) { }
                }
                passed.Add("running-export-prevents-plugin-unload");
                try
                {
                    if(!await exporting) throw new Exception(workflow.Progress);
                    if(workflow.Running || !workflow.Progress.Contains("临时文件清理失败")) throw new Exception("Cleanup failure did not restore export state");
                    passed.Add("temporary-cleanup-failure-restores-export-state");
                }
                finally {held?.Dispose();if(temporary!=null && Directory.Exists(temporary)) Directory.Delete(temporary,true);}
                if(workflow.Destination!=Path.Combine(Path.GetFullPath(output),"export")) throw new Exception("Export destination changed during execution");
                if(!File.Exists(Path.Combine(workflow.Destination,"extra.txt"))) throw new Exception("Export step missing");
                passed.Add("export-contribution-executed");
                if(!documents.Draft(entry).Dirty || documents.Current!=sourceDocument || Directory.Exists(documents.Location)) throw new Exception("Export saved or replaced author drafts");
                if(BoardGame.Authoring.ProjectView.Version(BoardGame.Authoring.ProjectPaths.Read(sourceDocument.DirectoryPath!))!=BoardGame.Authoring.ProjectView.Version(original)) throw new Exception("Export modified source files");
                passed.Add("draft-and-source-remain-unsaved");
                if(BoardGame.Runtime.GameRuntime.Load(Path.Combine(workflow.Destination,"game.bgm")).Fingerprint!=expected || workflow.Source?.Version!=view.Source.Version) throw new Exception("Export did not consume declared draft version");
                passed.Add("export-matches-declared-draft-fingerprint");
                await app.Disable("probe.export-step");
                if(app.Service<EditorContributions>("contributions").Entries.Any(e=>e.Kind=="export-step")) throw new Exception("Export contribution survived disable");
                passed.Add("plugin-disable-removes-export-contribution");
                if(Directory.GetFiles(workflow.Destination,"*",SearchOption.AllDirectories).Any(p=>Path.GetFileName(p).StartsWith("CordiSharp.") && Path.GetFileName(p)!="CordiSharp.dll" || Path.GetFileName(p).StartsWith("Editor"))) throw new Exception("Editor dependency exported");
                passed.Add("export-excludes-editor-dependencies");
                if(!await workflow.Run(false)) throw new Exception("Preflight failed: "+workflow.Progress);
                passed.Add("valid-release-preflight");
                var actual=workflow.ReleasePath;workflow.ReleasePath=Path.Combine(output,"missing-release");
                if(await workflow.Run(false) || !workflow.Progress.Contains("失败")) throw new Exception("Missing dependency not diagnosed");workflow.ReleasePath=actual;
                passed.Add("missing-release-diagnosed");
                documents.DiscardDraft(entry);await app.DisposeAsync();
            }
            catch(Exception e) {errors.Add(e.ToString());}
            finally
            {
                try
                {
                    host.Application.Root.Get<DocumentWorkspace>("documents")?.Set(null);
                    await host.Application.DisposeAsync();
                    passed.Add("application-cleanup-completed");
                }
                catch(Exception cleanup) {errors.Add("Cleanup: "+cleanup);}
                File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new {success=errors.Count==0,errors,passed}));
                host.Window.Close();
            }
        };
        return host.Window;
    }
}
