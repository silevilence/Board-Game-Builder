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
            var errors=new List<string>();Directory.CreateDirectory(output);
            try
            {
                await host.Ready;var app=host.Application;var documents=app.Service<DocumentWorkspace>("documents");
                documents.Location=Path.Combine(Path.GetFullPath(output),"author");
                var workflow=app.Service<ExportWorkflow>("exports");workflow.ReleasePath=release;workflow.Destination=Path.Combine(Path.GetFullPath(output),"export");
                FileStream? held=null;string? temporary=null;
                app.Install(new("probe.export-step",1,[],["contributions"],scope=>scope.Service<EditorContributions>("contributions").Register(scope,"export-step","probe",(Action<ExportContext>)(c=>
                {
                    File.WriteAllText(Path.Combine(c.Destination,"extra.txt"),"probe");temporary=c.ModuleDirectory;
                    held=File.Open(Path.Combine(temporary,BoardGame.Runtime.GameRuntime.Entry),FileMode.Open,FileAccess.Read,FileShare.Read);
                }))));
                await app.Enable("probe.export-step");
                var exporting=workflow.Run(true);
                foreach(var id in new[]{"probe.export-step","author.export"})
                {
                    try {await app.Disable(id);throw new Exception("Running export was unloaded");}
                    catch(InvalidOperationException e) when(e.Message.Contains("执行")) { }
                }
                try
                {
                    if(!await exporting) throw new Exception(workflow.Progress);
                    if(workflow.Running || !workflow.Progress.Contains("临时文件清理失败")) throw new Exception("Cleanup failure did not restore export state");
                }
                finally {held?.Dispose();if(temporary!=null && Directory.Exists(temporary)) Directory.Delete(temporary,true);}
                if(workflow.Destination!=Path.Combine(Path.GetFullPath(output),"export")) throw new Exception("Export destination changed during execution");
                if(!File.Exists(Path.Combine(workflow.Destination,"extra.txt"))) throw new Exception("Export step missing");
                await app.Disable("probe.export-step");
                if(app.Service<EditorContributions>("contributions").Entries.Any(e=>e.Kind=="export-step")) throw new Exception("Export contribution survived disable");
                if(Directory.GetFiles(workflow.Destination,"*",SearchOption.AllDirectories).Any(p=>Path.GetFileName(p).StartsWith("CordiSharp.") && Path.GetFileName(p)!="CordiSharp.dll" || Path.GetFileName(p).StartsWith("Editor"))) throw new Exception("Editor dependency exported");
                if(!await workflow.Run(false)) throw new Exception("Preflight failed: "+workflow.Progress);
                var actual=workflow.ReleasePath;workflow.ReleasePath=Path.Combine(output,"missing-release");
                if(await workflow.Run(false) || !workflow.Progress.Contains("失败")) throw new Exception("Missing dependency not diagnosed");workflow.ReleasePath=actual;
                await app.DisposeAsync();
            }
            catch(Exception e) {errors.Add(e.ToString());}
            finally
            {
                File.WriteAllText(Path.Combine(output,"result.json"),Wire.Json(new {success=errors.Count==0,errors}));
                await host.Application.DisposeAsync();host.Window.Close();
            }
        };
        return host.Window;
    }
}
