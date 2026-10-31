using Avalonia.Controls;
using Avalonia.Layout;
using BoardGame.Authoring;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class RuntimeConfigurationPanel:UserControl,IDisposable
{
    private readonly DocumentWorkspace documents;
    private readonly RuntimeConfiguration configuration;
    private readonly AuthorRuntimeCatalog catalog;
    private readonly ApplicationSessions sessions;
    private readonly BoardSessionController board;
    private readonly EditorViewports viewports;
    private readonly EditorCommands commands;
    private readonly TextBlock graphics=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button rebuild=new(){Content="应用所选后端并重建视口"},cancel=new(){Content="取消图形重建"};
    private readonly TextBox entry=new(){AcceptsReturn=true,MinHeight=240,FontFamily=new("Consolas")};
    private readonly TextBox publication=new(){AcceptsReturn=true,MinHeight=140,FontFamily=new("Consolas")};
    private readonly TextBlock report=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBlock status=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly ComboBox implementations=new(){MinWidth=300};
    private bool refreshing;
    private string sessionIdentity="";
    private sealed record Choice(AuthorRuntimeImplementation Value){public override string ToString()=>$"{Value.Definition.Id} {Value.Definition.Version} · {Value.Origin}";}
    public RuntimeConfigurationPanel(EditorPluginScope scope)
    {
        documents=scope.Service<DocumentWorkspace>("documents");configuration=scope.Service<RuntimeConfiguration>("runtime.configuration");catalog=scope.Service<AuthorRuntimeCatalog>("runtime.catalog");
        sessions=scope.Service<ApplicationSessions>("sessions");board=scope.Service<BoardSessionController>("board.sessions");
        viewports=scope.Service<EditorViewports>("viewports");commands=scope.Service<EditorCommands>("commands");
        rebuild.Click+=async(_,_)=>await commands.Execute("graphics.rebuild");cancel.Click+=async(_,_)=>await commands.Execute("graphics.cancel");
        var choose=new Button{Content="启用／使用此版本"};var remove=new Button{Content="移除此插件选择"};var validate=new Button{Content="校验当前草稿"};
        var reset=new Button{Content="放弃入口草稿"};var publish=new Button{Content="创建作者发布配置"};
        choose.Click+=(_,_)=>Act(()=>configuration.Select(((Choice)implementations.SelectedItem!).Value));
        remove.Click+=(_,_)=>Act(()=>configuration.Remove(((Choice)implementations.SelectedItem!).Value.Definition.Id));
        reset.Click+=(_,_)=>Act(()=>documents.DiscardDraft(GameRuntime.Entry));validate.Click+=async(_,_)=>await configuration.Validate();
        publish.Click+=(_,_)=>Act(()=>PublicationEditing.Edit(documents,_=>{}));
        entry.TextChanged+=(_,_)=>{if(!refreshing && documents.Current!=null)documents.EditDraft(GameRuntime.Entry,entry.Text ?? "");};
        publication.TextChanged+=(_,_)=>{if(!refreshing && documents.Current?.Paths.Contains(AuthorPublication.FileName)==true)documents.EditDraft(AuthorPublication.FileName,publication.Text ?? "");};
        Content=new ScrollViewer{Content=new StackPanel{Margin=new(20),Spacing=12,Children={
            new TextBlock{Text="运行插件与发布配置",FontSize=20},new TextBlock{Text="选择只写入当前草稿；缺少配置字段时请在入口 JSON 补齐。现有会话继续使用启动时的快照。",TextWrapping=Avalonia.Media.TextWrapping.Wrap},
            new WrapPanel{Orientation=Orientation.Horizontal,Children={implementations,choose,remove,validate,reset}},status,
            new WrapPanel{Children={rebuild,cancel}},graphics,
            new TextBlock{Text=GameRuntime.Entry},entry,new TextBlock{Text=AuthorPublication.FileName+"（作者参数，不随游戏作为运行配置加载）"},publish,publication,report}}};
        documents.Changed+=Refresh;catalog.Changed+=Refresh;configuration.Changed+=Refresh;sessions.Changed+=RefreshSession;board.Changed+=RefreshSession;viewports.Changed+=RefreshGraphics;commands.Changed+=RefreshGraphics;Refresh();RefreshGraphics();
    }
    private void Act(Action action){try{action();status.Text="已更新草稿；可撤销，尚未保存。";}catch(Exception error){status.Text=error.Message;}}
    private void Refresh()
    {
        if(refreshing)return;refreshing=true;
        try
        {
            entry.IsEnabled=documents.Current?.Paths.Contains(GameRuntime.Entry)==true;
            var source=entry.IsEnabled?documents.Draft(GameRuntime.Entry).Text:"";if(entry.Text!=source)entry.Text=source;
            publication.IsEnabled=documents.Current?.Paths.Contains(AuthorPublication.FileName)==true;
            var settings=publication.IsEnabled?documents.Draft(AuthorPublication.FileName).Text:"";if(publication.Text!=settings)publication.Text=settings;
            var selected=(implementations.SelectedItem as Choice)?.Value.Definition;
            try
            {
                var choices=configuration.Available().Select(p=>new Choice(p)).ToArray();implementations.ItemsSource=choices;
                implementations.SelectedItem=choices.FirstOrDefault(p=>selected!=null && p.Value.Definition.Id==selected.Id && p.Value.Definition.Version==selected.Version) ?? choices.FirstOrDefault();
            }
            catch{implementations.ItemsSource=Array.Empty<Choice>();}
            report.Text=configuration.Describe();status.Text=configuration.Validation;
        }
        finally{refreshing=false;}
    }
    private void RefreshSession()
    {
        var identity=sessions.SourceView?.Source.Version+"/"+board.SourceView?.Source.Version;
        if(identity==sessionIdentity)return;sessionIdentity=identity;Refresh();
    }
    private void RefreshGraphics(){graphics.Text=viewports.Status+"\n当前宿主实际激活："+(viewports.ActualBackends is {Length:>0} active?active:"无（视口未挂载或已释放）");rebuild.IsEnabled=commands.CanExecute("graphics.rebuild");cancel.IsEnabled=commands.CanExecute("graphics.cancel");}
    public void Dispose(){documents.Changed-=Refresh;catalog.Changed-=Refresh;configuration.Changed-=Refresh;sessions.Changed-=RefreshSession;board.Changed-=RefreshSession;viewports.Changed-=RefreshGraphics;commands.Changed-=RefreshGraphics;}
}
