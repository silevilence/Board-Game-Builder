using Avalonia;
using Avalonia.Controls;
using BoardGame.Authoring;
namespace BoardGame.Editor;

public sealed class ExportPanel : UserControl,IDisposable
{
    private readonly ExportWorkflow workflow;
    private readonly ComboBox providers=new(){HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
    private readonly StackPanel fields=new(){Spacing=8};
    private readonly TextBlock description=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBlock progress=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button preflight=new(){Content="依赖预检"};
    private readonly Button export=new(){Content="导出当前草稿"};
    private readonly Button cancel=new(){Content="取消"};
    private bool refreshing;
    private string identity="";
    private sealed record Choice(string Id,string Title){public override string ToString()=>Title;}
    public ExportPanel(EditorPluginScope scope)
    {
        workflow=scope.Service<ExportWorkflow>("exports");
        Content=new ScrollViewer{Content=new StackPanel{Margin=new Thickness(20),Spacing=12,Children={new TextBlock{Text="导出方式"},providers,description,fields,
            new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10,Children={preflight,export,cancel}},progress}}};
        providers.SelectionChanged+=(_,_)=>{if(!refreshing && providers.SelectedItem is Choice choice)workflow.Selected=choice.Id;};
        preflight.Click+=async(_,_)=>await workflow.Run(false);export.Click+=async(_,_)=>await workflow.Run(true);cancel.Click+=(_,_)=>workflow.Cancel();
        workflow.Changed+=Refresh;Refresh();
    }
    private void Refresh()
    {
        refreshing=true;
        try
        {
            var available=workflow.Providers.Items;var selected=available.SingleOrDefault(p=>p.Id==workflow.Selected);
            var key=string.Join(";",available.Select(p=>p.Id))+"/"+workflow.Selected+"/"+workflow.Generation;
            if(identity!=key)
            {
                identity=key;var choices=available.Select(p=>new Choice(p.Id,p.Title)).ToList();
                if(selected==null && workflow.Selected.Length>0)choices.Add(new(workflow.Selected,workflow.Selected+"（未启用，配置保留）"));
                providers.ItemsSource=choices;providers.SelectedItem=choices.SingleOrDefault(p=>p.Id==workflow.Selected);
                fields.Children.Clear();
                if(selected!=null)foreach(var parameter in selected.Operation.Parameters)
                {
                    var id=selected.Id;var name=parameter.Key;
                    fields.Children.Add(new TextBlock{Text=name+(parameter.Value.Required?" *":"")+" — "+parameter.Value.Description,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
                    var input=new TextBox{Text=workflow.Parameter(id,name)};
                    input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)workflow.SetParameter(id,name,input.Text ?? "");};fields.Children.Add(input);
                }
            }
            description.Text=selected==null?"选择已启用的导出提供者；缺失提供者的配置会保留。":selected.Description+"\n产物："+selected.ArtifactKind+"；目标："+string.Join(", ",selected.Platforms)+"\n前提："+string.Join("；",selected.Dependencies);
            providers.IsEnabled=fields.IsEnabled=!workflow.Running;
            preflight.IsEnabled=export.IsEnabled=!workflow.Running && selected!=null;cancel.IsEnabled=workflow.Running;progress.Text=workflow.Progress;
        }
        finally {refreshing=false;}
    }
    public void Dispose()=>workflow.Changed-=Refresh;
}
