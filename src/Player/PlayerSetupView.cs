using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Player;

// Presentation-only navigation. Choices always submit a currently offered authoritative action.
public sealed class PlayerSetupView : UserControl,IDisposable
{
    private readonly PlayerSetup setup;
    private readonly ResourcePool resources;
    private readonly Func<InteractionIntent,long,Task> submit;
    private readonly Dictionary<string,Bitmap> images=[];
    private SessionFrame? frame;
    private int step;
    private bool busy,disposed;
    public int CurrentStep=>step;
    public PlayerSetupView(PlayerSetup setup,ResourcePool resources,Func<InteractionIntent,long,Task> submit)
    {this.setup=setup;this.resources=resources;this.submit=submit;Background=new SolidColorBrush(Color.Parse("#142923"));}
    public bool Show(SessionFrame value)
    {
        if(disposed)return false;
        frame=value;
        IsVisible=!value.Cpu && !value.View.Ended && value.Fault==null
            && value.View.Entities.FirstOrDefault(e=>e.Id==setup.Entity)?.Properties.TryGetValue(setup.Property,out var current)==true && current==setup.Value;
        if(IsVisible)Render();return IsVisible;
    }
    private void Render()
    {
        if(frame==null || disposed)return;
        var spec=setup.Steps[step];
        var body=new StackPanel {Spacing=18,MaxWidth=780,Margin=new Thickness(40),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        body.Children.Add(new TextBlock {Text=step==0?"◆  ◆  ◆":$"{step} / {setup.Steps.Length-1}",Foreground=Brushes.Tan,FontSize=16});
        body.Children.Add(new TextBlock {Text=spec.Title,FontSize=step==0?44:30,Foreground=Brushes.Beige,TextWrapping=TextWrapping.Wrap});
        body.Children.Add(new TextBlock {Text=spec.Text,FontSize=16,Foreground=Brushes.Wheat,TextWrapping=TextWrapping.Wrap});
        if(spec.PreviewEntity is {} id && frame.View.Entities.FirstOrDefault(e=>e.Id==id) is {} entity)
        {
            if(entity.Resource is {} name)
            {
                if(!images.TryGetValue(name,out var bitmap))
                {using var stream=new MemoryStream(resources.GetFile(resources.GetImage(name).Resource).ToArray());images[name]=bitmap=new Bitmap(stream);}
                body.Children.Add(new Image {Source=bitmap,Height=220,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left});
            }
            body.Children.Add(new TextBlock {Text="当前选择 · "+entity.Face,Foreground=Brushes.Beige,FontSize=18});
        }
        var choices=new WrapPanel();
        foreach(var offer in frame.Actions.Where(a=>spec.ActionKinds.Contains(a.Command.Kind,StringComparer.Ordinal)))
        {
            var selected=frame.View.Entities.FirstOrDefault(e=>e.Id==setup.Entity)?.Properties.TryGetValue(offer.Command.Kind,out var value)==true && value==offer.Command.Value;
            var button=Button((selected?"✓  ":"")+offer.Label);
            var revision=frame.Revision;
            button.Click+=async (_,_)=>await Choose(offer.Id,revision);choices.Children.Add(button);
        }
        body.Children.Add(choices);
        var navigation=new WrapPanel {Margin=new Thickness(0,12,0,0)};
        if(step>0){var back=Button("返回");back.Click+=(_,_)=>{step--;Render();};navigation.Children.Add(back);}
        var next=Button(spec.NextLabel);next.Name="SetupNext";
        var action=spec.SubmitKind==null?null:frame.Actions.FirstOrDefault(a=>a.Command.Kind==spec.SubmitKind);
        next.IsEnabled=!busy && (spec.SubmitKind==null?step+1<setup.Steps.Length:action!=null);
        var currentRevision=frame.Revision;
        next.Click+=async (_,_)=>{if(action!=null)await Choose(action.Id,currentRevision);else if(step+1<setup.Steps.Length){step++;Render();}};
        navigation.Children.Add(next);body.Children.Add(navigation);
        Content=new ScrollViewer {Content=body,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
    }
    private Button Button(string label)=>new(){Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsEnabled=!busy,Margin=new Thickness(0,0,10,10),Padding=new Thickness(20,12),FontSize=16};
    private async Task Choose(string id,long revision)
    {
        if(busy || disposed)return;busy=true;Render();
        try{await submit(new("action",Action:id),revision);}
        finally{busy=false;if(!disposed && IsVisible)Render();}
    }
    public void Dispose(){disposed=true;Content=null;frame=null;foreach(var bitmap in images.Values)bitmap.Dispose();images.Clear();}
}
