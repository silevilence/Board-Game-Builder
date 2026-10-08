using Avalonia.Controls;
using Avalonia.Threading;
using CordiSharp.Registry;

namespace BoardGame.Editor;

// Optional format UI is a child CordiSharp fiber. Document drafts remain in the foundation plugin.
public sealed class FormatPanel : UserControl
{
    public FormatPanel(EditorPluginScope owner,Func<EditorPluginScope,Control> factory)
    {
        PluginHandle? child=null;bool? supported=null;
        var gate=new SemaphoreSlim(1,1);
        owner.OnDocumentChanged(()=>_ = Refresh());
        _=Refresh();
        async Task Refresh()
        {
            await gate.WaitAsync();
            try
            {
                if(owner.Cancellation.IsCancellationRequested) return;
                var next=owner.Document?.SupportsStructuredEditing ?? false;
                if(next==supported) return;supported=next;
                Content=null;
                if(child!=null) {await child.DisposeAsync();child=null;}
                Content=new TextBlock {Text="此定义格式没有专用作者界面。可在“游戏文件”中编辑，并使用试玩与导出。",TextWrapping=Avalonia.Media.TextWrapping.Wrap};
                if(!next) return;
                child=owner.Context.Inject(Array.Empty<string>(),(context,_)=>Mount(context));
                await child.Await();
            }
            catch(Exception error) {supported=null;Content=new TextBlock {Text=error.Message,TextWrapping=Avalonia.Media.TextWrapping.Wrap};}
            finally {gate.Release();}
        }
        async Task Mount(CordiSharp.Context context)
        {
            var scope=owner.ForContext(context,true);
            await Dispatcher.UIThread.InvokeAsync(()=>Content=factory(scope));
        }
    }
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
