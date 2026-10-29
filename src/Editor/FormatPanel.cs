using Avalonia.Controls;
using Avalonia.Threading;
using CordiSharp.Registry;

namespace BoardGame.Editor;

// Optional format UI is a child CordiSharp fiber. Document drafts remain in the foundation plugin.
public sealed class FormatPanel : UserControl,IAsyncDisposable
{
    private Task pending=Task.CompletedTask;
    private bool disposed;
    public Task Ready=>pending;
    public static FormatPanel Board(EditorPluginScope owner,Func<EditorPluginScope,Control> factory)=>new(owner,factory,
        ()=>owner.Document?.SupportsAuthorCapability(BoardGame.Authoring.GameAuthorFormats.BoardModel)==true,
        ()=>"当前作者格式未提供桌游模型编辑能力；仍可使用游戏文件、通用配置与试玩。");
    public FormatPanel(EditorPluginScope owner,Func<EditorPluginScope,Control> factory):this(owner,factory,null) { }
    public FormatPanel(EditorPluginScope owner,Func<EditorPluginScope,Control> factory,Func<bool>? supports,Func<string>? unavailableText=null)
    {
        PluginHandle? child=null;bool? supported=null;
        var documents=owner.Service<DocumentWorkspace>("documents");
        (long Generation,long Formats,string? Definition)? mounted=null;
        var gate=new SemaphoreSlim(1,1);
        owner.Context.Effect(()=>this);
        void Schedule() {if(!disposed && !owner.Cancellation.IsCancellationRequested) pending=Refresh();}
        owner.OnDocumentChanged(Schedule);
        Schedule();
        async Task Refresh()
        {
            var entered=false;
            try
            {
                await gate.WaitAsync(owner.Cancellation);entered=true;
                if(disposed || owner.Cancellation.IsCancellationRequested) return;
                var next=supports?.Invoke() ?? (owner.Document?.SupportsStructuredEditing ?? false);
                var identity=(documents.Generation,documents.AuthorFormats.Revision,documents.Current?.AuthorFormat?.DefinitionPath);
                if(next==supported && mounted==identity)
                {
                    if(!next && unavailableText!=null && Content is TextBlock message) message.Text=unavailableText();
                    return;
                }
                supported=next;
                mounted=identity;
                Content=null;
                if(child!=null) {await child.DisposeAsync().AsTask().WaitAsync(owner.Cancellation);child=null;}
                if(disposed || owner.Cancellation.IsCancellationRequested || documents.Generation!=identity.Generation || documents.AuthorFormats.Revision!=identity.Revision) return;
                Content=new TextBlock {Text=unavailableText?.Invoke() ?? "此定义格式没有专用作者界面。可在“游戏文件”中编辑，并使用试玩与导出。",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
                if(!next) return;
                child=owner.Context.Inject(Array.Empty<string>(),(context,_)=>Mount(context,identity));
                await child.Await().WaitAsync(owner.Cancellation);
            }
            catch(OperationCanceledException) when(owner.Cancellation.IsCancellationRequested) { }
            catch(Exception error) {if(!disposed && !owner.Cancellation.IsCancellationRequested) {supported=null;Content=new TextBlock {Text=error.Message,TextWrapping=Avalonia.Media.TextWrapping.Wrap};}}
            finally {if(entered) gate.Release();}
        }
        async Task Mount(CordiSharp.Context context,(long Generation,long Revision,string? DefinitionPath) identity)
        {
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(disposed || owner.Cancellation.IsCancellationRequested || documents.Generation!=identity.Generation || documents.AuthorFormats.Revision!=identity.Revision) return;
                var scope=owner.ForDocumentContext(context,supports==null,supports);
                Content=factory(scope);
                if(Content is IDisposable resource) scope.Own(resource);
            });
        }
    }
    public async ValueTask DisposeAsync() {disposed=true;await pending;}
}

public sealed class GameFilePanel : UserControl
{
    public GameFilePanel(EditorPluginScope scope)
    {
        var documents=scope.Service<DocumentWorkspace>("documents");
        var paths=new ComboBox();var text=new TextBox {AcceptsReturn=true,FontFamily=Avalonia.Media.FontFamily.Parse("Consolas")};
        var discard=new Button {Content="放弃当前文件草稿"};
        var top=new StackPanel {Children={paths,discard}};var root=new DockPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);root.Children.Add(new ScrollViewer {Content=text});Content=root;
        var loading=false;string? selected=null;
        void Refresh()
        {
            loading=true;
            try
            {
                var files=documents.Current?.Paths.Where(p=>new[]{".json",".lua",".yaml",".yml",".txt",".md"}.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray() ?? [];
                paths.ItemsSource=files;
                selected=files.Contains(selected)?selected:files.FirstOrDefault();paths.SelectedItem=selected;
                text.IsEnabled=selected!=null;text.Text=selected==null?"":documents.Draft(selected).Text;
            }
            finally {loading=false;}
        }
        paths.SelectionChanged+=(_,_)=>{if(!loading) {selected=paths.SelectedItem as string;Refresh();}};
        text.PropertyChanged+=(_,e)=>{if(!loading && selected!=null && e.Property==TextBox.TextProperty) documents.EditDraft(selected,text.Text ?? "");};
        discard.Click+=(_,_)=>{if(selected!=null) documents.DiscardDraft(selected);};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
}
