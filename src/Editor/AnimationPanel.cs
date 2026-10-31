using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Authoring;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

namespace BoardGame.Editor;

public sealed class AnimationPanel : UserControl
{
    internal InteractiveTable? PreviewTable {get;private set;}
    public AnimationPanel(EditorPluginScope scope)
    {
        var documents=scope.Service<DocumentWorkspace>("documents");
        var workspace=scope.Service<AnimationWorkspace>("animation.author");
        var source=new TextBox{Name="animationDefinition",AcceptsReturn=true,MinHeight=240,FontFamily=FontFamily.Parse("Consolas")};
        var clips=new ComboBox{Name="animationClip",MinWidth=160};
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var apply=new Button{Name="animationApply",Content="应用动画草稿"};
        var discard=new Button{Content="放弃字段"};
        var preview=new Button{Name="animationPreview",Content="预览当前字段"};
        var stop=new Button{Content="停止预览"};
        var host=new ContentControl{MinHeight=300};
        var tools=new WrapPanel{Children={apply,discard,clips,preview,stop}};
        var root=new DockPanel();var top=new StackPanel{Children={tools,new TextBlock{Text="关键帧 timeMs / x / y / rotation：相对最终位置的偏移。source 指定起点对象；末帧必须回到 (0,0,0)。预览只读，不运行对局。",TextWrapping=TextWrapping.Wrap},status,source}};
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);root.Children.Add(host);Content=root;
        var loading=false;var generation=documents.Generation;
        void Stop() {PreviewTable?.Dispose();PreviewTable=null;host.Content=null;}
        void Refresh()
        {
            if(loading)return;loading=true;
            try
            {
                if(generation!=documents.Generation) {Stop();generation=documents.Generation;}
                var draft=workspace.Draft();if(source.Text!=draft.Text)source.Text=draft.Text;
                try
                {
                    var definition=AnimationFile.Parse(draft.Text);var selected=clips.SelectedItem as string;
                    clips.ItemsSource=definition.Clips.Select(c=>c.Id).ToArray();clips.SelectedItem=definition.Clips.Any(c=>c.Id==selected)?selected:definition.Clips.FirstOrDefault()?.Id;
                    status.Text=draft.Dirty?"尚未应用；预览不会保存或推进对局。":"已应用到文档；显式保存后写入磁盘。";
                }
                catch(Exception error) {clips.ItemsSource=Array.Empty<string>();status.Text=error.Message;}
            }
            catch(Exception error) {Stop();status.Text=error.Message;}
            finally {loading=false;}
        }
        source.PropertyChanged+=(_,e)=> {if(!loading && e.Property==TextBox.TextProperty) {loading=true;try {Stop();documents.EditForm(AnimationWorkspace.FormKey,source.Text ?? "");}finally {loading=false;}Refresh();}};
        apply.Click+=(_,_)=> {try {workspace.Apply();}catch(Exception error) {status.Text=error.Message;}};
        discard.Click+=(_,_)=>documents.DiscardForm(AnimationWorkspace.FormKey);
        stop.Click+=(_,_)=>Stop();
        preview.Click+=(_,_)=>
        {
            Stop();
            try
            {
                var view=workspace.View();var draft=workspace.Draft();
                if(draft.Baseline!=view.Source.Version)throw new InvalidOperationException("工程版本已改变，请先协调字段。");
                var definition=AnimationFile.Parse(draft.Text);var selected=clips.SelectedItem as string;
                if(selected==null)throw new InvalidOperationException("先配置并选择一个动画片段。");
                using var snapshot=new ProjectSnapshot(view,new Dictionary<string,byte[]?>{[AnimationOperations.Path(view)]=Encoding.UTF8.GetBytes(AnimationFile.Write(definition))});
                var module=GameRuntime.Load(snapshot.Directory);
                var scene=TableScene.From(new GameEngine(module.Initial).Project(module.Initial.Seats[0].Id),module.Resources);
                var table=scope.Service<BoardViewports>("board.viewports").Create(scope);table.InputEnabled=false;PreviewTable=table;host.Content=table;
                table.RenderingError+=message=> {status.Text=message;Stop();};
                table.Show(scene with {AnimationCues=definition.Clips.ToDictionary(c=>c.Id,c=>c.Id==selected?1:0)});
                status.Text="只读预览 · "+selected;
            }
            catch(Exception error) {Stop();status.Text=error.Message;}
        };
        scope.Own(new Cleanup(Stop));scope.OnDocumentChanged(Refresh);Refresh();
    }
}

public static class AnimationEditorPlugin
{
    public static EditorPluginDefinition Definition=>new("author.animation",1,["animation.author"],["documents","panels","commands","board.viewports"],scope=>
    {
        var documents=scope.Service<DocumentWorkspace>("documents");var workspace=new AnimationWorkspace(documents);scope.Provide("animation.author",workspace);
        var commands=scope.Service<EditorCommands>("commands");
        commands.Register(scope,new("animation.create","新建动画库",async ()=>
        {
            var view=workspace.View();var path="animations.json";var suffix=1;while(view.Exists(path))path=$"animations-{suffix++}.json";
            await workspace.Create(path,scope.Cancellation);
        },()=>documents.Current!=null && !documents.HasPendingForms));
        scope.OnDocumentChanged(commands.Refresh);
        bool Supported() {try {if(documents.Current==null)return false;_=AnimationOperations.Path(workspace.View());return true;}catch {return false;}}
        scope.Service<PanelContributions>("panels").Register(scope,"author.animation","动画设计",()=>new FormatPanel(scope,child=>new AnimationPanel(child),Supported,()=>"请先打开工程并通过“新建动画库”命令启用动画；也可在运行插件配置中声明已有动画。"));
    });
}
