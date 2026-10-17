using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;

namespace BoardGame.View;

// Local presentation state. Navigation never calls the game or changes a replay.
public sealed class DocumentShelf : UserControl,IDisposable
{
    private readonly ResourcePool resources;
    private readonly DocumentPanel[] panels;
    private readonly int[] pages;
    private readonly Dictionary<string,Bitmap> images=new(StringComparer.Ordinal);
    private readonly Dictionary<int,(Window Window,DocumentShelf Shelf)> auxiliary=[];
    private readonly DockPanel root=new();
    private readonly Border paper=new();
    private readonly TextBlock title=new() {TextWrapping=TextWrapping.Wrap,FontSize=14};
    private readonly Button previous=new(){Content="‹"};
    private readonly Button next=new(){Content="›"};
    private SeatView? view;
    private int selected;
    private bool disposed;
    public int CurrentPage=>pages.Length==0?0:pages[selected];
    public string CurrentPanel=>panels.Length==0?"":panels[selected].Id;
    public IReadOnlyList<string> VisibleText {get;private set;}=[];
    public DocumentShelf(ResourcePool resources)
    {
        this.resources=resources;panels=resources.Documents?.Panels.ToArray() ?? [];pages=new int[panels.Length];
        IsVisible=panels.Length>0;Content=root;if(panels.Length==0)return;
        selected=Array.FindIndex(panels,p=>p.Permanent);if(selected<0)selected=0;
        var tabs=new WrapPanel();
        for(var i=0;i<panels.Length;i++)
        {
            var index=i;var button=new Button {Content=panels[i].Title,Margin=new Thickness(2),Padding=new Thickness(8,5)};
            button.Click+=(_,_)=>Select(index);tabs.Children.Add(button);
        }
        DockPanel.SetDock(tabs,Dock.Top);root.Children.Add(tabs);
        var navigation=new DockPanel{Margin=new Thickness(4,6)};
        previous.Click+=(_,_)=>Turn(-1);next.Click+=(_,_)=>Turn(1);
        DockPanel.SetDock(previous,Dock.Left);DockPanel.SetDock(next,Dock.Right);
        navigation.Children.Add(previous);navigation.Children.Add(next);navigation.Children.Add(title);
        DockPanel.SetDock(navigation,Dock.Top);root.Children.Add(navigation);
        root.Children.Add(new ScrollViewer {Content=paper,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        Margin=new Thickness(4);Background=new SolidColorBrush(Color.Parse("#252824"));
        Width=panels[selected].Width;
    }
    public void Show(SeatView value) {if(disposed)return;view=value;Render();foreach(var child in auxiliary.Values)child.Shelf.Show(value);}
    public void Select(int index)
    {
        if(disposed || index<0 || index>=panels.Length)return;
        if(!panels[index].Permanent && panels.Any(p=>p.Permanent) && TopLevel.GetTopLevel(this) is Window owner)
        {
            if(auxiliary.TryGetValue(index,out var opened)){opened.Window.Activate();return;}
            var content=new DocumentShelf(new ResourcePool(resources.Files,resources.Images,resources.Binaries,resources.Font,documents:new(1,[panels[index] with {Permanent=true,Width=480}]))){FontFamily=FontFamily};
            var window=new Window{Title=panels[index].Title,Width=520,Height=780,Content=content};auxiliary[index]=(window,content);
            window.Closed+=(_,_)=>{auxiliary.Remove(index);content.Dispose();};
            if(view!=null)content.Show(view);window.Show(owner);return;
        }
        selected=index;Width=panels[selected].Width;Render();
    }
    public void Turn(int delta)
    {
        if(disposed || panels.Length==0)return;
        pages[selected]=Math.Clamp(pages[selected]+delta,0,panels[selected].Pages.Count-1);Render();
    }
    private void Render()
    {
        if(disposed || view==null || panels.Length==0)return;
        var panel=panels[selected];var page=panel.Pages[pages[selected]];
        title.Text=$"{page.Title} · {pages[selected]+1}/{panel.Pages.Count}";
        previous.IsEnabled=pages[selected]>0;next.IsEnabled=pages[selected]+1<panel.Pages.Count;
        if(!images.TryGetValue(page.Image,out var image))
        {
            using var stream=new MemoryStream(resources.GetFile(resources.GetImage(page.Image).Resource).ToArray());
            images[page.Image]=image=new Bitmap(stream);
        }
        var canvas=new Canvas {Width=page.Width,Height=page.Height};
        canvas.Children.Add(new Image {Source=image,Width=page.Width,Height=page.Height,Stretch=Stretch.Fill});
        var texts=new List<string>();
        foreach(var field in page.Fields)
        {
            var text=DocumentProjection.Text(field,view);if(text.Length==0)continue;texts.Add(text);
            var block=new TextBlock {Text=text,FontSize=field.Size,Foreground=new SolidColorBrush(Color.Parse(field.Color))};
            Canvas.SetLeft(block,field.X);Canvas.SetTop(block,field.Y);canvas.Children.Add(block);
        }
        VisibleText=texts.AsReadOnly();paper.Child=new Viewbox {Stretch=Stretch.Uniform,Child=canvas,Width=Width-8};
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;view=null;paper.Child=null;root.Children.Clear();VisibleText=[];
        foreach(var child in auxiliary.Values.ToArray()){child.Window.Close();child.Shelf.Dispose();}auxiliary.Clear();
        foreach(var image in images.Values)image.Dispose();images.Clear();
    }
}
