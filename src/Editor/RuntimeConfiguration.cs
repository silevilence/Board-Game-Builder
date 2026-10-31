using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class RuntimeConfiguration(DocumentWorkspace documents,AuthorRuntimeCatalog catalog,ApplicationSessions sessions,BoardSessionController board):IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task pending=Task.CompletedTask;
    private long operation;
    public string Validation {get;private set;}="";
    public event Action? Changed;
    private ProjectIdentity Identity=>new(documents.Current?.DirectoryPath!,"editor.runtime","gui",documents.WorkspaceId,documents.Generation);
    public ProjectView View()=>documents.PreviewAuthorView(Identity);
    public RuntimeStartup Startup()=>JsonSerializer.Deserialize<RuntimeStartup>(View().Text(GameRuntime.Entry),AuthorProfiles.Json) ?? throw new InvalidDataException("game.start.json: missing entry");
    public IReadOnlyList<AuthorRuntimeImplementation> Available()=>catalog.Read(Startup());
    public void Select(AuthorRuntimeImplementation implementation)
    {
        var entry=JsonNode.Parse(View().Text(GameRuntime.Entry))!.AsObject();var list=entry["plugins"]!.AsArray();var definition=implementation.Definition;
        var matches=list.OfType<JsonObject>().Where(p=>p["id"]?.GetValue<string>()==definition.Id).ToArray();
        if(matches.Length>1)throw new InvalidDataException("请先修复重复选择："+definition.Id);
        var selection=matches.SingleOrDefault() ?? new JsonObject{["id"]=definition.Id,["configuration"]=new JsonObject()};
        selection["version"]=definition.Version;selection["api"]=definition.Api;
        if(definition.Implementation is {} fingerprint)selection["implementation"]=fingerprint;else selection.Remove("implementation");
        if(matches.Length==0)list.Add(selection);
        documents.EditDraft(GameRuntime.Entry,entry.ToJsonString(new(){WriteIndented=true}));
    }
    public void Remove(string id)
    {
        var entry=JsonNode.Parse(View().Text(GameRuntime.Entry))!.AsObject();var list=entry["plugins"]!.AsArray();
        foreach(var selection in list.Where(p=>p?["id"]?.GetValue<string>()==id).ToArray())list.Remove(selection);
        documents.EditDraft(GameRuntime.Entry,entry.ToJsonString(new(){WriteIndented=true}));
    }
    public string Describe()
    {
        var lines=new List<string>();
        try
        {
            var view=View();var startup=JsonSerializer.Deserialize<RuntimeStartup>(view.Text(GameRuntime.Entry),AuthorProfiles.Json)!;var available=catalog.Read(startup);
            lines.Add("工程草稿版本："+view.Source.Version[..12]+"；保存、试玩和发布各自捕获此内容。");
            foreach(var selection in startup.Plugins)
            {
                lines.Add($"\n文件声明：{selection.Id} {selection.Version} / API {selection.Api} / {selection.Implementation ?? "内置固定实现"}");
                var matched=available.Where(p=>p.Definition.Id==selection.Id && p.Definition.Version==selection.Version && p.Definition.Api==selection.Api && p.Definition.Implementation==selection.Implementation).ToArray();
                if(matched.Length!=1){lines.Add("不可用：缺少匹配的已安装实现或实现指纹不一致；原配置保留。");continue;}
                var item=matched[0];var definition=item.Definition;
                lines.Add($"已安装：{item.Origin}；{definition.Execution.Scope} / {definition.Execution.Modes}");
                lines.Add("提供："+string.Join(", ",definition.Provides)+"；依赖："+string.Join(", ",definition.Requires));
                lines.Add("配置字段："+(item.Manifest is {ContentFields:null}?"包未提供字段元数据，请使用校验确认配置":string.Join(", ",definition.Content?.Fields ?? [])));
                foreach(var callback in definition.Execution.Contributions ?? [])lines.Add($"{callback.Phase} {definition.Id}/{callback.Id}；先于 [{string.Join(", ",callback.Before ?? [])}]；后于 [{string.Join(", ",callback.After ?? [])}]");
                if(item.Manifest is {} manifest)lines.Add("平台："+string.Join(", ",manifest.Targets)+"；AOT："+(manifest.Aot==null?"未声明":manifest.Aot.Compatible?"发布者声明兼容，仍需真实编译及运行验证":"不兼容："+manifest.Aot.Reason));
            }
            try
            {
                var selected=startup.Plugins.Select(p=>available.Single(a=>a.Definition.Id==p.Id && a.Definition.Version==p.Version && a.Definition.Api==p.Api && a.Definition.Implementation==p.Implementation)).ToArray();
                var definitions=selected.Select(p=>p.Definition).ToArray();
                if(selected.All(p=>p.Manifest is not {ContentFields:null}))RuntimeDiscovery.Validate(startup,definitions);
                var graph=RuntimeHost.Validate(startup,definitions,[],HostMode.Headless);
                lines.Add("\n无界面初始化顺序："+string.Join(" → ",graph.Select(p=>p.Id)));
                lines.Add("所选图形后端："+string.Join(", ",definitions.Where(p=>p.Provides.Contains("render.backend")).Select(p=>p.Id)));
            }
            catch(Exception error){lines.Add("入口诊断："+error.Message);}
            if(view.Exists(AuthorPublication.FileName))
            {
                var settings=AuthorPublication.Read(view);lines.Add("作者发布配置："+(settings["selected"]?.GetValue<string>() ?? "未选方式")+"；仅含发布参数，不启用运行插件。");
            }
            else lines.Add("作者发布配置：尚未创建；导出面板默认参数尚未保存。");
        }
        catch(Exception error){lines.Add("配置诊断："+(error is AuthorOperationException author?author.Path+": ":"")+error.Message+"；原始文件与草稿仍可编辑。");}
        foreach(var active in new[]{sessions.SourceView,board.SourceView}.OfType<ProjectView>())
        {
            lines.Add("\n当前会话实际快照："+active.Source.Version[..12]+"；配置修改不会自动替换运行实例。");
            try {foreach(var plugin in JsonSerializer.Deserialize<RuntimeStartup>(active.Text(GameRuntime.Entry),AuthorProfiles.Json)!.Plugins)
                lines.Add($"运行中：{plugin.Id} {plugin.Version} / API {plugin.Api} / {plugin.Implementation ?? "内置固定实现"}");}
            catch(Exception error){lines.Add("会话身份诊断："+error.Message);}
        }
        return string.Join("\n",lines);
    }
    public Task Validate()
    {
        cancellation?.Cancel();var token=new CancellationTokenSource();cancellation=token;var current=++operation;var generation=documents.Generation;
        async Task Run()
        {
            try
            {
                var view=documents.AuthorView(Identity);var runtime=documents.Runtimes.Resolve(view);
                await Task.Run(()=>runtime.Validate(view,"content",token.Token),token.Token);
                if(current==operation && generation==documents.Generation)Validation="校验通过；草稿版本 "+view.Source.Version[..12]+"，磁盘未保存。";
            }
            catch(Exception error){if(current==operation && generation==documents.Generation)Validation="校验失败："+error.Message;}
            finally{if(ReferenceEquals(cancellation,token))cancellation=null;token.Dispose();Changed?.Invoke();}
        }
        var next=Run();pending=Task.WhenAll(pending,next);return next;
    }
    public void DocumentReplaced(){operation++;cancellation?.Cancel();Validation="";Changed?.Invoke();}
    public async ValueTask DisposeAsync(){DocumentReplaced();await pending;Changed=null;}
}
