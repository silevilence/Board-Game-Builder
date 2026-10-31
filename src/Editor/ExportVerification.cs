using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Authoring;

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
                var workflow=app.Service<ExportWorkflow>("exports");workflow.Selected="player-directory";workflow.SetParameter(workflow.Selected,"player",release);workflow.Destination=Path.Combine(Path.GetFullPath(output),"export");
                view=documents.AuthorView(new(sourceDocument.DirectoryPath!,"editor.export","gui",documents.WorkspaceId,documents.Generation));
                var commands=app.Service<EditorCommands>("commands");
                foreach(var info in workflow.Providers.Items)
                    if(!AuthorHost.Builtins.Single(p=>p.Id==info.Plugin).Operations.Any(o=>Wire.Json(o)==Wire.Json(info.Operation)))throw new Exception("CLI/GUI export descriptions differ");
                await app.Disable("author.export.package");
                if(workflow.Providers.Items.Count!=1 || commands.Items.Any(c=>c.Id=="project.pack"))throw new Exception("Package provider did not disable independently");
                passed.Add("player-only-discovery-and-cli-descriptions");
                FileStream? held=null;string? temporary=null;
                app.Install(new("probe.export-step",1,[],["contributions"],scope=>scope.Service<EditorContributions>("contributions").Register(scope,"export-step","probe",(Action<ExportContext>)(c=>
                {
                    File.WriteAllText(Path.Combine(c.Destination,"extra.txt"),"probe");temporary=c.ModuleDirectory;
                    held=File.Open(Path.Combine(temporary,BoardGame.Runtime.GameRuntime.Entry),FileMode.Open,FileAccess.Read,FileShare.Read);
                }))));
                await app.Enable("probe.export-step");
                var exporting=workflow.Run(true);
                if(exporting.IsCompleted && !await exporting) throw new Exception(workflow.Progress);
                foreach(var id in new[]{"probe.export-step","author.export","author.export.player"})
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
                workflow.Destination=Path.Combine(Path.GetFullPath(output),"preflight-new");
                if(!await workflow.Run(false)) throw new Exception("Preflight failed: "+workflow.Progress);
                passed.Add("valid-release-preflight");
                var actual=workflow.Parameter(workflow.Selected,"player");workflow.SetParameter(workflow.Selected,"player",Path.Combine(output,"missing-release"));
                if(await workflow.Run(false) || !workflow.Progress.Contains("失败")) throw new Exception("Missing dependency not diagnosed");workflow.SetParameter(workflow.Selected,"player",actual);
                passed.Add("missing-release-diagnosed");
                await app.Enable("author.export.package");await app.Disable("author.export.player");
                if(workflow.Parameter("player-directory","player")!=release || commands.Items.Any(c=>c.Id=="player.export"))throw new Exception("Disabled Player lost settings or retained command");
                if(await workflow.Run(false) || !workflow.Progress.Contains("未启用"))throw new Exception("Missing selected provider was silently replaced");
                workflow.Selected="game-package";workflow.Destination=Path.Combine(Path.GetFullPath(output),"draft.bgm");
                if(!await workflow.Run(true))throw new Exception(workflow.Progress);
                if(BoardGame.Runtime.GameRuntime.Load(workflow.Destination).Fingerprint!=expected || !documents.Draft(entry).Dirty)throw new Exception("Package export lost draft identity");
                var target=workflow.Destination;
                for(var cycle=0;cycle<3;cycle++)
                {
                    await app.Disable("author.export.package");await app.Enable("author.export.package");
                    if(workflow.Destination!=target || commands.Items.Count(c=>c.Id=="project.pack")!=1 || workflow.Providers.Items.Count!=1)throw new Exception("Reload duplicated operation or lost configuration");
                }
                await app.Enable("author.export.player");
                if(workflow.Providers.Items.Count!=2 || workflow.Parameter("player-directory","player")!=release)throw new Exception("Re-enable lost independent provider state");
                passed.Add("package-only-draft-export-missing-provider-settings-and-three-reloads");
                var shell=app.Service<WorkspaceShell>("workspace");shell.Tabs.SelectedItem=shell.Tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,"author.export"));
                await Task.Delay(150);
                using(var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)host.Window.Bounds.Width,(int)host.Window.Bounds.Height),new(96,96)))
                {bitmap.Render(host.Window);bitmap.Save(Path.Combine(output,"providers.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
                documents.DiscardDraft(entry);
                var delayed=new DelayedExporter();
                app.Install(new("probe.export-delay",1,[],["exports"],scope=>scope.OwnAsync(scope.Service<ExportWorkflow>("exports").RegisterProvider(delayed))));
                await app.Enable("probe.export-delay");workflow.Selected=delayed.Info.Id;
                var late=workflow.Run(true);await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                documents.Set(ModuleDocument.Create());
                if(await late || workflow.Source!=null || workflow.Progress.Length!=0)throw new Exception("Late export notification changed replacement document");
                passed.Add("document-switch-cancels-export-and-ignores-late-results");
                delayed.Started=new(TaskCreationOptions.RunContinuationsAsynchronously);workflow.Selected=delayed.Info.Id;
                var shutdown=workflow.Run(true);await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await app.DisposeAsync();if(await shutdown)throw new Exception("Forced shutdown did not cancel export");
                passed.Add("shutdown-cancels-awaits-and-disposes-active-provider");
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
    private sealed class DelayedExporter:IAuthorExporter
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AuthorExportInfo Info=>new("probe-delayed","probe.export-delay","1",1,"Delayed","probe","Cancellation probe",[],[],new("probe.export","probe.export-delay","probe",new(),new(),[],[]));
        public async Task<AuthorExportCheck> Preflight(AuthorExportRequest request)
        {Started.TrySetResult();await Task.Delay(Timeout.Infinite,request.Cancellation);throw new Exception("Cancellation ignored");}
        public Task<AuthorArtifact> Export(AuthorExportRequest request)=>throw new Exception("Unexpected export");
    }
}
