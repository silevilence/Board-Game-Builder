using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CordiSharp.Plugins.Avalonia;

namespace BoardGame.Editor;

public sealed class WorkspaceShell : UserControl
{
    public TabControl Tabs { get; }=new();
    public WrapPanel Toolbar { get; }=new() {Margin=new Thickness(6)};
    private readonly TextBlock status=new() {Margin=new Thickness(8),TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    public WorkspaceShell()
    {
        Slot.SetId(Tabs,"editor.panels");
        var layout=new DockPanel();DockPanel.SetDock(Toolbar,Dock.Top);DockPanel.SetDock(status,Dock.Bottom);
        layout.Children.Add(Toolbar);layout.Children.Add(status);layout.Children.Add(Tabs);Content=layout;
    }
    public void Report(string message)=>status.Text=message;
}

public sealed class PanelContributions(AvaloniaUIService ui)
{
    private readonly HashSet<string> ids=new(StringComparer.Ordinal);
    public int Count=>ids.Count;
    public void Register(EditorPluginScope owner,string id,string title,Func<Control> factory)
    {
        ui.OnUi(()=>
        {
            if(!ids.Add(id)) throw new InvalidDataException("Duplicate panel: "+id);
            Control? control=null;
            try
            {
                control=factory();
                var tab=new TabItem {Header=title,Content=control,Tag=id};
                owner.Own(new Cleanup(()=> {ids.Remove(id);(control as IDisposable)?.Dispose();tab.Content=null;}));
                ui.RegisterSlot(owner.Context,"editor.panels",new FuncDataTemplate<object>((_,_)=>null),()=>new[]{tab});
            }
            catch {ids.Remove(id);(control as IDisposable)?.Dispose();throw;}
        });
    }
}
