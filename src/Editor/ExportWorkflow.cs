using Avalonia.Threading;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class ExportWorkflow(DocumentWorkspace documents,EditorContributions contributions)
{
    public string ReleasePath { get; set; }=EditorPaths.ReleasePlayer;
    public string Destination { get; set; }=EditorPaths.InWorkspace("artifacts/export-m25");
    public string Progress { get; private set; }="";
    public bool Running { get; private set; }
    public event Action? Changed;
    public async Task<bool> Run(bool write)
    {
        if(Running) return false;
        Running=true;Changed?.Invoke();
        using var lease=contributions.Acquire("export-step");
        var temp=Path.Combine(Path.GetTempPath(),"bgb-export-"+Guid.NewGuid());
        try
        {
            var doc=documents.Current ?? throw new InvalidOperationException("请打开并保存模块");
            documents.Save();if(doc.DirectoryPath==null) throw new InvalidOperationException("请先设置模块保存目录");
            ModuleDocument.Open(doc.DirectoryPath).Save(temp);
            var player=ReleasePath;var output=Destination;
            var steps=contributions.Entries.Where(e=>e.Kind=="export-step").ToArray();
            var reporter=new Progress<string>(text=> {if(Running) {Progress=text;Changed?.Invoke();}});
            await Task.Run(()=>
            {
                if(write) PlayerDistribution.Export(player,temp,output,staging=>Dispatcher.UIThread.Invoke(()=>
                {
                    foreach(var step in steps)
                    {
                        if(step.Value is Action<ExportContext> action) action(new(temp,staging));
                        else throw new InvalidDataException("Invalid export step: "+step.Id);
                    }
                }),reporter);
                else _=PlayerDistribution.Preflight(player,temp);
            });
            Progress=write?"导出完成。双击输出目录中的 Play.exe。":"依赖预检通过。";return true;
        }
        catch(Exception error) {Progress="导出失败："+error.Message;return false;}
        finally
        {
            try {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
            catch(Exception error) {Progress+=" 临时文件清理失败："+error.Message;}
            finally {Running=false;Changed?.Invoke();}
        }
    }
}
