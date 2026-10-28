using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BoardGame.Resources;
using BoardGame.Presentation;

namespace BoardGame.Presentation;

// Local presentation state. Navigation never calls the game or changes a replay.
public class PageShelf : UserControl,IDisposable
{
    private readonly AssetSet resources;
    private PagePanel[] panels;
    private readonly PageNavigator cursor;
    private readonly Dictionary<string,Bitmap> images=new(StringComparer.Ordinal);
    private readonly Dictionary<int,(Window Window,PageShelf Shelf)> auxiliary=[];
    private readonly DockPanel root=new();
    private readonly Border paper=new();
    private readonly TextBlock title=new() {TextWrapping=TextWrapping.Wrap,FontSize=14};
    private readonly Button previous=new(){Content="‹"};
    private readonly Button next=new(){Content="›"};
    private bool shown;
    private int selected=>cursor.Selected;
    private bool disposed;
    public int CurrentPage=>cursor.CurrentPage;
    public string CurrentPanel=>panels.Length==0?"":panels[selected].Id;
    public IReadOnlyList<string> VisibleText {get;private set;}=[];
    public PageShelf(AssetSet resources,IReadOnlyList<PagePanel> content)
    {
        this.resources=resources;panels=Freeze(content);
        var initial=Array.FindIndex(panels,p=>p.Permanent);cursor=new(panels.Select(p=>p.Pages.Count),Math.Max(0,initial));
        IsVisible=panels.Length>0;Content=root;if(panels.Length==0)return;
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
    public void Show(IReadOnlyList<PagePanel> content)
    {
        if(disposed)return;var next=Freeze(content);
        if(next.Length!=panels.Length || next.Where((p,i)=>p.Id!=panels[i].Id || p.Pages.Count!=panels[i].Pages.Count).Any())
            throw new InvalidOperationException("Page structure changed; rebuild the shelf");
        panels=next;shown=true;Render();
        foreach(var child in auxiliary)child.Value.Shelf.Show([panels[child.Key] with {Permanent=true,Width=480}]);
    }
    private static PagePanel[] Freeze(IReadOnlyList<PagePanel> content)
    {
        var copy=content.Select(p=>p with {Pages=p.Pages.Select(page=>page with {Fields=page.Fields.ToArray()}).ToArray()}).ToArray();
        if(copy.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=copy.Length || copy.Any(p=>string.IsNullOrWhiteSpace(p.Id) || p.Pages.Count==0 || p.Width<16))
            throw new ArgumentException("Invalid page panels");
        foreach(var page in copy.SelectMany(p=>p.Pages))
            if(!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width<=0 || page.Height<=0
                || page.Fields.Any(f=>!double.IsFinite(f.X) || !double.IsFinite(f.Y) || !double.IsFinite(f.Size) || f.Size<=0))
                throw new ArgumentException("Invalid page layout");
        return copy;
    }
    public void Select(int index)
    {
        if(disposed || index<0 || index>=panels.Length)return;
        if(!panels[index].Permanent && panels.Any(p=>p.Permanent) && TopLevel.GetTopLevel(this) is Window owner)
        {
            if(auxiliary.TryGetValue(index,out var opened)){opened.Window.Activate();return;}
            var content=new PageShelf(resources,[panels[index] with {Permanent=true,Width=480}]){FontFamily=FontFamily};
            var window=new Window{Title=panels[index].Title,Width=520,Height=780,Content=content};auxiliary[index]=(window,content);
            window.Closed+=(_,_)=>{auxiliary.Remove(index);content.Dispose();};
            if(shown)content.Show([panels[index] with {Permanent=true,Width=480}]);window.Show(owner);return;
        }
        cursor.Select(index);Width=panels[selected].Width;Render();
    }
    public void Turn(int delta)
    {
        if(disposed || panels.Length==0)return;
        cursor.Turn(delta);Render();
    }
    private void Render()
    {
        if(disposed || !shown || panels.Length==0)return;
        var panel=panels[selected];var page=panel.Pages[CurrentPage];
        title.Text=$"{page.Title} · {CurrentPage+1}/{panel.Pages.Count}";
        previous.IsEnabled=CurrentPage>0;next.IsEnabled=CurrentPage+1<panel.Pages.Count;
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
            var text=field.Text;if(text.Length==0)continue;texts.Add(text);
            var block=new TextBlock {Text=text,FontSize=field.Size,Foreground=new SolidColorBrush(Color.Parse(field.Color))};
            Canvas.SetLeft(block,field.X);Canvas.SetTop(block,field.Y);canvas.Children.Add(block);
        }
        VisibleText=texts.AsReadOnly();paper.Child=new Viewbox {Stretch=Stretch.Uniform,Child=canvas,Width=Width-8};
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;shown=false;paper.Child=null;root.Children.Clear();VisibleText=[];
        foreach(var child in auxiliary.Values.ToArray()){child.Window.Close();child.Shelf.Dispose();}auxiliary.Clear();
        foreach(var image in images.Values)image.Dispose();images.Clear();
    }
}
