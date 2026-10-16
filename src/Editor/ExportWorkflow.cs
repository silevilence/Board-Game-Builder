using Avalonia.Threading;
using BoardGame.Runtime;
using BoardGame.Authoring;

namespace BoardGame.Editor;

public sealed class ExportWorkflow(DocumentWorkspace documents,EditorContributions contributions)
{
    public string ReleasePath { get; set; }=EditorPaths.ReleasePlayer;
    public string Destination { get; set; }=EditorPaths.InWorkspace("artifacts/export-m25");
    public string Progress { get; private set; }="";
    public bool Running { get; private set; }
    public ProjectSource? Source { get; private set; }
    public event Action? Changed;
    public async Task<bool> Run(bool write)
    {
        if(Running) return false;
        Running=true;Source=null;Changed?.Invoke();
        using var lease=contributions.Acquire("export-step");
        ProjectSnapshot? snapshot=null;
        try
        {
            var doc=documents.Current ?? throw new InvalidOperationException("请打开模块");
            await using var mode=new WorkspaceEditMode(documents,new(doc.DirectoryPath!,"editor.export","gui",documents.WorkspaceId,documents.Generation));
            var view=await mode.Read();Source=view.Source;snapshot=new(view);var temp=snapshot.Directory;
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
            Progress=(write?"导出完成。双击输出目录中的 Play.exe。":"依赖预检通过。")+" 来源版本 "+Source.Version[..8]+"；工程草稿未保存。";return true;
        }
        catch(Exception error) {Progress="导出失败："+error.Message;return false;}
        finally
        {
            try {snapshot?.Dispose();}
            catch(Exception error) {Progress+=" 临时文件清理失败："+error.Message;}
            finally {Running=false;Changed?.Invoke();}
        }
    }
}
