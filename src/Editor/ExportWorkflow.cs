using Avalonia.Threading;
using BoardGame.Authoring;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoardGame.Editor;

public sealed class ExportWorkflow : IAsyncDisposable
{
    private readonly DocumentWorkspace documents;
    private readonly EditorContributions contributions;
    private CancellationTokenSource? cancellation;
    private long operation;
    private Task<bool>? pending;
    public AuthorExports Providers {get;}
    public long Generation=>documents.Generation;
    public string? ActiveProvider {get;private set;}
    public string Selected {get=>documents.ViewState.GetValueOrDefault("export.selected","");set {documents.ViewState["export.selected"]=value;Changed?.Invoke();}}
    public string Destination {get=>Parameter(Selected,"target");set=>SetParameter(Selected,"target",value);}
    public string Progress {get;private set;}="";
    public bool Running {get;private set;}
    public ProjectSource? Source {get;private set;}
    public event Action? Changed;
    public ExportWorkflow(DocumentWorkspace documents,EditorContributions contributions,AuthorExports providers)
    {this.documents=documents;this.contributions=contributions;Providers=providers;providers.Changed+=Refresh;documents.Replaced+=Replaced;}
    public string Parameter(string provider,string key)=>documents.ViewState.GetValueOrDefault("export."+provider+"."+key,"");
    public void SetParameter(string provider,string key,string value) {documents.ViewState["export."+provider+"."+key]=value;}
    public void Defaults(string provider,IReadOnlyDictionary<string,string> values)
    {
        foreach(var entry in values)documents.ViewState.TryAdd("export."+provider+"."+entry.Key,entry.Value);
        if(Selected.Length==0)Selected=provider;
    }
    public void Cancel()=>cancellation?.Cancel();
    private void Replaced(){Cancel();Progress="";Source=null;Refresh();}
    private void Refresh()=>Changed?.Invoke();
    public Task<bool> Run(bool write)
    {
        if(Running)return Task.FromResult(false);
        pending=RunCore(write);return pending;
    }
    private async Task<bool> RunCore(bool write)
    {
        if(Running)return false;
        var id=Selected;var generation=documents.Generation;var currentOperation=++operation;
        using var token=new CancellationTokenSource();cancellation=token;
        Running=true;ActiveProvider=id;Source=null;Changed?.Invoke();
        using var lease=contributions.Acquire("export-step");
        bool Current()=>generation==documents.Generation && currentOperation==operation;
        try
        {
            var info=Providers.Items.SingleOrDefault(p=>p.Id==id) ?? throw new InvalidOperationException("提供者未启用："+id+"；配置已保留。");
            var arguments=new JsonObject();
            foreach(var parameter in info.Operation.Parameters)
            {
                var text=Parameter(id,parameter.Key);if(text.Length==0 && !parameter.Value.Required)continue;
                arguments[parameter.Key]=parameter.Value.Type=="string"?JsonValue.Create(text):JsonNode.Parse(text);
            }
            var doc=documents.Current ?? throw new InvalidOperationException("请打开工程");
            await using var mode=new WorkspaceEditMode(documents,new(doc.DirectoryPath!,"editor.export","gui",documents.WorkspaceId,generation));
            var view=await mode.Read(token.Token);Source=view.Source;
            // Capture an independent runtime catalog before yielding; plugin removal cannot change this operation's runtime.
            var runtimes=new AuthorRuntimes();using var runtime=runtimes.Register(documents.Runtimes.Resolve(view));
            var steps=contributions.Entries.Where(e=>e.Kind=="export-step").ToArray();
            var reporter=new Progress<string>(text=>{if(Running && Current()){Progress=text;Changed?.Invoke();}});
            var request=new AuthorExportRequest(view,runtimes,JsonSerializer.SerializeToElement(arguments),token.Token,reporter,(snapshot,staging)=>Dispatcher.UIThread.Invoke(()=>
            {
                token.Token.ThrowIfCancellationRequested();
                foreach(var step in steps)
                    if(step.Value is Action<ExportContext> action)action(new(snapshot,staging));
                    else throw new InvalidDataException("Invalid export step: "+step.Id);
            }));
            var message=await Task.Run(async()=>
            {
                if(write)
                {
                    var artifact=await Providers.Export(id,request);
                    return "导出完成："+artifact.Target+"。"+info.Description+" "+string.Join(" ",artifact.Warnings);
                }
                var check=await Providers.Preflight(id,request);
                if(!check.Available)throw new AuthorOperationException("export.preflight",string.Join("; ",check.Diagnostics.Select(d=>d.Message)));
                return "依赖预检通过。";
            });
            if(Current())Progress=message+" 来源版本 "+view.Source.Version[..8]+"；工程草稿未保存。";
            return true;
        }
        catch(OperationCanceledException){if(Current())Progress="导出已取消。";return false;}
        catch(Exception error){if(Current())Progress="导出失败："+error.Message;return false;}
        finally {cancellation=null;Running=false;ActiveProvider=null;Changed?.Invoke();}
    }
    public IAsyncDisposable RegisterProvider(IAuthorExporter provider)=>new ProviderLifetime(this,provider.Info.Id,Providers.Register(provider));
    private sealed class ProviderLifetime(ExportWorkflow workflow,string id,IDisposable registration):IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if(workflow.ActiveProvider==id){workflow.Cancel();if(workflow.pending!=null)await workflow.pending;}
            registration.Dispose();
        }
    }
    public async ValueTask DisposeAsync()
    {Cancel();if(pending!=null)await pending;Providers.Changed-=Refresh;documents.Replaced-=Replaced;}
}
