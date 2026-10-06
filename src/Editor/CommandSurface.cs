using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using BoardGame.View;

namespace BoardGame.Editor;

public sealed class CommandSurface : IDisposable
{
    private readonly Window window;
    private readonly WorkspaceShell shell;
    private readonly EditorCommands commands;
    private readonly Menu menu=new();
    private readonly List<Button> buttons=[];
    private readonly List<CommandBinding> bindings=[];
    public CommandSurface(Window window,WorkspaceShell shell,EditorCommands commands)
    {
        this.window=window;this.shell=shell;this.commands=commands;
        shell.Toolbar.Children.Insert(0,menu);commands.Changed+=Refresh;window.KeyDown+=KeyDown;Refresh();
    }
    private void Refresh()
    {
        if(bindings.Select(b=>b.Id).SequenceEqual(commands.Items.Select(c=>c.Id))) {foreach(var b in bindings) b.Refresh();return;}
        foreach(var button in buttons) shell.Toolbar.Children.Remove(button);
        buttons.Clear();bindings.Clear();menu.Items.Clear();
        var group=new MenuItem {Header="命令"};menu.Items.Add(group);
        foreach(var command in commands.Items)
        {
            var binding=new CommandBinding(commands,command.Id,shell.Report);bindings.Add(binding);
            var button=new Button {Content=command.Title,Command=binding};buttons.Add(button);shell.Toolbar.Children.Add(button);
            group.Items.Add(new MenuItem {Header=command.Title,Command=binding,InputGesture=command.Shortcut==null?null:KeyGesture.Parse(command.Shortcut)});
        }
    }
    private async void KeyDown(object? sender,KeyEventArgs e)
    {
        if(e.Handled) return;
        var ancestors=(e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().ToArray() ?? [];
        foreach(var command in commands.Items)
        {
            if(command.Shortcut==null || !KeyGesture.Parse(command.Shortcut).Matches(e)) continue;
            if(ancestors.OfType<TextBox>().Any() && command.Focus!=CommandFocus.Global) return;
            var viewport=ancestors.Any(a=>a is InteractiveTable || a.GetType().Name=="PlayPanel");
            if(command.Focus==CommandFocus.Author && viewport || command.Focus==CommandFocus.Viewport && !viewport) return;
            e.Handled=true;
            try {await commands.Execute(command.Id);} catch(Exception error) {shell.Report(error.Message);}
            return;
        }
    }
    public void Dispose()
    {
        commands.Changed-=Refresh;window.KeyDown-=KeyDown;
        foreach(var button in buttons) shell.Toolbar.Children.Remove(button);
        menu.Items.Clear();shell.Toolbar.Children.Remove(menu);bindings.Clear();buttons.Clear();
    }
    private sealed class CommandBinding(EditorCommands registry,string id,Action<string> report) : ICommand
    {
        public string Id=>id;
        public bool CanExecute(object? parameter)=>registry.CanExecute(id);
        public async void Execute(object? parameter) {try {await registry.Execute(id);} catch(Exception e) {report(e.Message);}}
        public event EventHandler? CanExecuteChanged;
        public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
    }
}
